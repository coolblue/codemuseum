import UIKit
import SceneKit
import ARKit

@available(iOS 11.3, *)
protocol ARViewDelegate: class {
  func didLoadView()
  func didPlaceModel()
  func didTapOnScene()
  func didTapBack()
  func didTapClose()
  func didFocusOnSurface()
  func didFocusOutSurface()
}

protocol ARViewProtocol: class {
  var viewData: ARViewData? { get set }
  func embedActions(_ controller: UIViewController)
  func embedInstructions(controller: UIViewController)
}

@available(iOS 11.3, *)
class ARViewController: UIViewController {

  @IBOutlet private weak var sceneView: ARSCNView!
  @IBOutlet private weak var footerContainer: UIView!
  @IBOutlet private weak var headerContainer: UIView!
  @IBOutlet private var gestures: [UIGestureRecognizer]!

  weak var delegate: ARViewDelegate?

  var viewData: ARViewData? {
    didSet {
      guard let viewData = viewData else { return }

      if viewData.alignment != oldValue?.alignment {
        updateAlignment(viewData.alignment)
      }

      if viewData.vibrate != oldValue?.vibrate, viewData.vibrate {
        SystemSoundService.play(systemSoundType: .peek)
      }

      if viewData.shouldRemoveModel {
        model.runAction(SCNAction.fadeOut(duration: 0.3)) {
          self.model.removeFromParentNode()
        }
      }

      model.scale(by: viewData.modelSize)
    }
  }

  // MARK: Private Properties

  private lazy var model: Model = {
    return Model(name: "tv")
  }()

  private var currentAngleY: Float = 0.0
  private var currentPosition: CGPoint?
  private var featurePointModel = FeaturePointNode()
  private var featurePoints: [FeaturePointNode] = []
  private let configuration = CoolblueAR.configurationType.init()
  private var tappedNode: SCNNode?

  // MARK: Lifecycle

  override func viewDidLoad() {
    super.viewDidLoad()

    sceneView.delegate = self
    sceneView.automaticallyUpdatesLighting = true
    sceneView.autoenablesDefaultLighting = true

    delegate?.didLoadView()
  }

  override func viewDidAppear(_ animated: Bool) {
    super.viewDidAppear(animated)

    let headerActionsViewController = buildHeaderActionsViewController()
    headerActionsViewController.delegate = self
    addChildViewController(headerActionsViewController, animated: false, embedIn: headerContainer)
  }

  override func viewWillAppear(_ animated: Bool) {
    super.viewWillAppear(animated)

    sceneView.session.delegate = self
    sceneView.session.run(configuration)
  }

  override var preferredStatusBarStyle: UIStatusBarStyle {
    return .lightContent
  }

  override func viewWillDisappear(_ animated: Bool) {
    super.viewWillDisappear(animated)

    sceneView.session.pause()
  }

  @objc private func checkVisiblePlanes() {
    let screenCenter = CGPoint(x: view.bounds.width/2, y: view.bounds.height/2)
    let hitTest = sceneView.hitTest(screenCenter, types: .existingPlaneUsingGeometry)
    if hitTest.count > 0 {
      delegate?.didFocusOnSurface()
    } else {
      delegate?.didFocusOutSurface()
    }
  }

  // MARK: Actions

  @IBAction func didPan(_ sender: UIPanGestureRecognizer) {
    let location: CGPoint = sender.location(in: sceneView)
    let translation = sender.translation(in: sceneView)

    switch sender.state {
    case .began:
      let hitTestResults = self.sceneView.hitTest(location, options: nil)
      guard let node = hitTestResults.first?.node else { return }
      tappedNode = node
    case .changed:

      let position = CGPoint(sceneView.projectPoint(model.position))
      let newPoint = CGPoint(x: position.x + translation.x, y: position.y + translation.y)
      let hitTest = sceneView.hitTest(newPoint, types: .existingPlane).first
      let cameraTransform = sceneView.session.currentFrame?.camera.transform

      model.move(node: tappedNode,
                 translation: translation,
                 position: position,
                 worldTransform: hitTest?.worldTransform,
                 cameraTransform: cameraTransform)
      sender.setTranslation(.zero, in: sceneView)


    case .ended:
      fallthrough

    default:
      tappedNode = nil
    }
  }

  @IBAction func didRotate(_ sender: UIRotationGestureRecognizer) {
    let rotation = Float(sender.rotation)

    switch sender.state {
    case .changed:
      model.eulerAngles.y = currentAngleY - rotation

    case .ended:
      currentAngleY = model.eulerAngles.y

    default:
      break
    }
  }

  @IBAction func didTap(_ sender: UITapGestureRecognizer) {

    delegate?.didTapOnScene()

    let tapLocation = sender.location(in: sceneView)

    if !model.hasParent {
      let hitTestResults = sceneView.hitTest(tapLocation, types: .existingPlaneUsingGeometry)

      guard let hitTestResult = hitTestResults.first else { return }
      guard let anchor = hitTestResult.anchor as? ARPlaneAnchor else { return }

      model.load(initialSize: viewData?.modelSize ?? .twentyFour)
      model.align(relativeTo: anchor,
                  camera: sceneView.session.currentFrame!.camera,
                  worldTransfrom: hitTestResult.worldTransform)

      sceneView.scene.rootNode.addChildNode(model)
      model.runAction(SCNAction.fadeIn(duration: 0.3), completionHandler: nil)
      delegate?.didPlaceModel()
    }

    loadModelForTheCurrentSurface(location: tapLocation)
  }

  @IBAction func didTapReset(_ sender: Any) {
    sceneView.session.pause()
  }

  // MARK: Private

  private func buildHeaderActionsViewController() -> HeaderActionsViewController {
    let viewController: HeaderActionsViewController = UIStoryboard(storyboard: Storyboard.headerActions, bundle: .ar).instantiateViewController()
    return viewController
  }

  private func updateAlignment(_ alignment: ModelAlignment) {
    configuration.planeDetection = ARWorldTrackingConfiguration.PlaneDetection(alignment)
  }

  private func updateObjectPosition() {
    guard let position = currentPosition else { return }
    translate(model, position: position)
  }

  private func translate(_ object: Model, position: CGPoint) {
    guard
      let hitTest = sceneView.hitTest(position, types: .existingPlaneUsingGeometry).first,
      let cameraTransform = sceneView.session.currentFrame?.camera.transform
      else {
        return
    }
    model.setTransform(hitTest.worldTransform, relativeTo: cameraTransform)
  }

  private func closeSession() {
    sceneView.session.pause()
    sceneView.scene.rootNode.childNodes.forEach { $0.removeFromParentNode() }
  }

  private func loadModelForTheCurrentSurface(location: CGPoint) {
    let hitTestResults = sceneView.hitTest(location, types: .existingPlaneUsingExtent)

    guard let anchor = hitTestResults.first?.anchor as? ARPlaneAnchor else {
      return
    }
    let shouldHideBase = anchor.alignment == .vertical
    model.hideTheBase(shouldHideBase)
  }
}

@available(iOS 11.3, *)
extension ARViewController: HeaderActionsViewControllerDelegate {
  func didTapClose() {
    closeSession()
    delegate?.didTapClose()
  }

  func didTapBack() {
    delegate?.didTapBack()
  }
}

// MARK: ARViewProtocol

@available(iOS 11.3, *)
extension ARViewController: ARViewProtocol {

  func embedActions(_ controller: UIViewController) {
    addChildViewController(controller, animated: true, embedIn: footerContainer)
  }

  func removeActions(_ controller: UIViewController) {
    remove(childViewController: controller, animated: false, completion: nil)
  }

  func embedInstructions(controller: UIViewController) {
    add(childViewController: controller, animated: true, completion: nil)
  }

  func changeModelSize(scaleMultFactor: ScaleFactor) {
    model.scale(by: scaleMultFactor)
  }
}

// MARK: ARSCNViewDelegate

@available(iOS 11.3, *)
extension ARViewController: ARSCNViewDelegate {

  func renderer(_ renderer: SCNSceneRenderer, updateAtTime time: TimeInterval) {
    updateObjectPosition()
  }
}

@available(iOS 11.3, *)
extension ARViewController: ARSessionDelegate {
  func session(_ session: ARSession, didUpdate frame: ARFrame) {

    if !model.hasParent {
      checkVisiblePlanes()
    }

    guard let points = frame.rawFeaturePoints?.points,
              viewData?.showDots ?? true else { return }

    let featurePointModel = self.featurePointModel.clone()
    featurePoints.append(featurePointModel)

    points.forEach {
      featurePointModel.position = SCNVector3($0.x, $0.y, $0.z)
      sceneView.scene.rootNode.addChildNode(featurePointModel)
      featurePointModel.runAction(SCNAction.fadeOut(duration: 1.0))
    }
  }
}


