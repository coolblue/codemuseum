import Foundation
import CoolblueUIKit
import Formatters

class ReviewSectionCardPresenter {

  // MARK: Private properties

  private let maxNumberOfPros = 2
  private let maxNumberOfCons = 1
  private let maxRating = 10

  private let review: Review
  private let dateFormatter: DateTimeFormatterProtocol
  private let numberFormatter: NumberFormatterProtocol

  // MARK: Internal properties

  weak var view: ReviewSectionCardProtocol? {
    didSet {
      updateView()
    }
  }

  // MARK: Initializers

  init(review: Review,
       dateFormatter: DateTimeFormatterProtocol,
       numberFormatter: NumberFormatterProtocol) {
    self.review = review
    self.dateFormatter = dateFormatter
    self.numberFormatter = numberFormatter
  }

  // MARK: Private methods

  private func updateView() {
    guard let view = view else { return }

    let reviewTitle = "\"\(review.title)\""
    let average = review.rating / 2
    var averageText = ""
    if let averageString = numberFormatter.string(from: NSNumber(value: review.rating), format: .reviewAverage) {
      averageText = "\(averageString)/\(maxRating)"
    }
    var reviewText: String?
    let pros: [ProCon] = review.pros.map { .init(isPro: true, text: $0) }
    let cons: [ProCon] = review.cons.map { .init(isPro: false, text: $0) }
    let proCons: [ProCon] = [ProCon](pros.prefix(maxNumberOfPros) + cons.prefix(maxNumberOfCons))
    if proCons.isEmpty {
      reviewText = review.description
    }
    let creationDateString = dateFormatter.string(for: review.creationDate, with: .dayMonthNameYear)
    let authorText = "\(review.creatorName) | \(creationDateString)"

    let viewData = ReviewSectionCardViewData(
      title: reviewTitle,
      average: average,
      averageText: averageText,
      proCons: proCons,
      reviewText: reviewText,
      authorText: authorText
    )

    view.viewData = viewData
  }
}
