using System;
using System.Linq;
using Coolblue.Utilities.MonitoringEvents;
using Newtonsoft.Json;
using Timeslot.Exceptions;
using Timeslot.Models;
using Timeslot.Models.DomainEvents;
using Timeslot.Ports;
using Timeslot.Ports.Calculators;
using Timeslot.Ports.Persistence;

namespace Timeslot.UseCases
{
    internal class CreateVisitUseCase : ICreateVisitUseCase
    {
        private readonly IVisitStore _visitStore;
        private readonly ICompositeStopTimeCalculator _compositeStopTimeCalculator;
        private readonly IProductLookup _productLookup;
        private readonly IDomainEventStore _domainEventStore;
        private readonly IDetermineCarrierGroupUseCase _determineCarrierGroupUseCase;
        private readonly ICapacityCalculator _capacityCalculator;
        private readonly MonitoringEvents _monitoringEvents;
        private readonly IServiceNameConverter _serviceNameConverter;

        public CreateVisitUseCase(
            IVisitStore visitStore,
            ICompositeStopTimeCalculator compositeStopTimeCalculator,
            IProductLookup productLookup,
            IDomainEventStore domainEventStore,
            IDetermineCarrierGroupUseCase determineCarrierGroupUseCase,
            ICapacityCalculator capacityCalculator,
            IServiceNameConverter serviceNameConverter,
            MonitoringEvents monitoringEvents)

        {
            _visitStore = visitStore;
            _compositeStopTimeCalculator = compositeStopTimeCalculator;
            _productLookup = productLookup;
            _domainEventStore = domainEventStore;
            _determineCarrierGroupUseCase = determineCarrierGroupUseCase;
            _capacityCalculator = capacityCalculator;
            _monitoringEvents = monitoringEvents;
            _serviceNameConverter = serviceNameConverter;
        }

        /// <summary>
        /// Create a timeslot visit.
        /// </summary>
        /// <param name="address">The address to where the order will be delivered</param>
        /// <param name="products">The Products that are part of the order; they can contain installation services as sub-products as well</param>
        /// <param name="deliveryRestrictions">Number of restrictions that can affect the delivery, for example if the restriction is business hours, delivery should be done accordingly</param>
        /// <param name="services">The Service Codes that are send by DireXtion</param>
        /// <param name="externalReference">The Visit External Reference in Timeslot.Visit table</param>
        /// <param name="source">Specifies which client called this method</param>
        /// <param name="suggestedCarrierGroupId">The predetermined Carrier Group</param>
        /// <param name="forcedWeightInKilogram">The predetermined weight send by DireXtion</param>
        /// <returns>Visit Object</returns>
        public Visit Create(
            Address address,
            Product[] products,
            DeliveryRestriction[] deliveryRestrictions,
            string[] services,
            string externalReference,
            string source,
            int? suggestedCarrierGroupId,
            double? forcedWeightInKilogram)
        {
            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            if (products == null)
            {
                throw new ArgumentNullException(nameof(products));
            }

            var existingVisit = _visitStore.GetByExternalReference(externalReference);
            if (existingVisit != null)
            {
                throw new VisitAlreadyExistsException(externalReference);
            }

            address.Normalize();
            address.ThrowIfInvalid();

            try
            {
                var enrichedProducts = new ProductDetailsFiller(_serviceNameConverter).EnrichProducts(products, _productLookup);
                
                var isLiftingRequired = deliveryRestrictions is object && deliveryRestrictions.Any(d => d.DeliveryRestrictionType == Enums.DeliveryRestrictionType.RequiresLifting);
                var determineCarrierGroupResult = _determineCarrierGroupUseCase.DetermineCarrierGroup(address, enrichedProducts, suggestedCarrierGroupId, isLiftingRequired);

                var visitToCalculateStopTimeFor = new Visit
                {
                    CarrierGroup = determineCarrierGroupResult.CarrierGroup,
                    Address = address,
                    Products = enrichedProducts,
                    Services = services,
                };

                var totalStopTime = _compositeStopTimeCalculator.Calculate(visitToCalculateStopTimeFor);

                var totalCapacity = _capacityCalculator.Calculate(determineCarrierGroupResult.CarrierGroup, enrichedProducts, forcedWeightInKilogram);

                var visit = _visitStore.Create(
                    address,
                    enrichedProducts,
                    deliveryRestrictions,
                    services,
                    totalStopTime,
                    totalCapacity,
                    externalReference,
                    determineCarrierGroupResult.CarrierGroup);

                if (determineCarrierGroupResult.WholeVisit == false)
                {
                    visit.Status = ReconcilableVisitStatus.MultiStop;
                }

                if (visit.Status != ReconcilableVisitStatus.Ok)
                {
                    _visitStore.SetReconcilableStatusForVisit(visit.Reference, visit.Status);
                }

                _domainEventStore?.Store(new VisitCreatedDomainEvent(visit.Reference, source));

                _monitoringEvents.Logger.Information(
                    "Created visit with reference {visit_reference} for Products: {products}, Services: {services}, External Reference: {extRef}, Source: {source}, Stoptime: {stoptime}, Capacity: {totalCapacity}, Forced weight = {forcedWeight}",
                    visit.Reference,
                    JsonConvert.SerializeObject(enrichedProducts),
                    services,
                    externalReference,
                    source,
                    totalStopTime,
                    totalCapacity,
                    forcedWeightInKilogram.HasValue ? forcedWeightInKilogram.Value.ToString() : "-not set-");

                return visit;
            }
            catch (InvalidAddressException iae)
            {
                _monitoringEvents.Logger.Error(iae, iae.Message);
                throw;
            }
            catch (ProductWeightNullException e)
            {
                _monitoringEvents.Logger.Error(e, e.Message);
                throw;
            }
            catch (CarrierGroupNotSupportedException cge)
            {
                _monitoringEvents.Logger.Error(cge, "Failed to determine carrier for create visit. Message: {message}", cge.Message);
                throw;
            }
            catch (NoCarrierGroupForVisitException ncgfv)
            {
                _monitoringEvents.Logger.Error(ncgfv, "Failed to determine carrier for create visit. Message: {message}", ncgfv.Message);
                throw;
            }
            catch (Exception e)
            {
                _monitoringEvents.Logger.Error(e, "Failed to create visit. Message: {message}", e.Message);
                throw new CreateVisitFailureException(e);
            }
        }
    }
}
