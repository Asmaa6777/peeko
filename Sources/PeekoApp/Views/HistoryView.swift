import PeekoCore
import SwiftUI

struct HistoryView: View {
    @Environment(AppModel.self) private var model

    private let calendar = Calendar.current
    var weeks = 5

    var body: some View {
        let history = model.history
        let today = Date.now
        VStack(alignment: .leading, spacing: 14) {
            streakCard(history.overallStreak(today: today))

            PanelSection(title: "Last \(weeks) weeks") {
                heatmap(history, today: today).card()
            }

            if !model.tasks.isEmpty {
                PanelSection(title: "By task") {
                    VStack(spacing: 6) {
                        ForEach(model.tasks) { task in
                            taskRow(task, history: history, today: today)
                        }
                    }
                }
            }
        }
        .onAppear { model.loadHistoryIfNeeded() }
    }

    private func streakCard(_ streak: Int) -> some View {
        HStack(spacing: 12) {
            FaceView(mood: streak > 0 ? .beaming : .dozing, width: 40)
            VStack(alignment: .leading, spacing: 2) {
                Text(streak == 0 ? "Start a streak today" : streak == 1 ? "1 day in a row" : "\(streak) days in a row")
                    .font(.system(size: 16, weight: .bold, design: .rounded))
                Text(streak == 0 ? "Do one thing and Peeko will count it." : "with at least one thing done")
                    .font(Theme.mono())
                    .foregroundStyle(Theme.muted)
            }
            Spacer()
        }
        .card()
    }

    /// Columns are weeks, rows are weekdays, starting on the calendar's first weekday.
    private func heatmap(_ history: History, today: Date) -> some View {
        let todayStart = calendar.startOfDay(for: today)
        let weekdayOffset = (calendar.component(.weekday, from: todayStart) - calendar.firstWeekday + 7) % 7
        let lastWeekStart = calendar.date(byAdding: .day, value: -weekdayOffset, to: todayStart)!
        let firstDay = calendar.date(byAdding: .day, value: -7 * (weeks - 1), to: lastWeekStart)!
        let counts = (0..<(weeks * 7)).map { history.doneCount(on: calendar.date(byAdding: .day, value: $0, to: firstDay)!) }
        let peak = max(counts.max() ?? 0, 1)
        let symbols = calendar.veryShortWeekdaySymbols
        let order = (0..<7).map { (calendar.firstWeekday - 1 + $0) % 7 }

        return HStack(alignment: .top, spacing: 4) {
            VStack(spacing: 4) {
                ForEach(0..<7, id: \.self) { row in
                    Text(symbols[order[row]])
                        .font(Theme.mono(9))
                        .foregroundStyle(Theme.muted)
                        .frame(width: 12, height: 18)
                }
            }
            ForEach(0..<weeks, id: \.self) { week in
                VStack(spacing: 4) {
                    ForEach(0..<7, id: \.self) { row in
                        let index = week * 7 + row
                        let date = calendar.date(byAdding: .day, value: index, to: firstDay)!
                        cell(date: date, count: counts[index], peak: peak, today: todayStart)
                    }
                }
            }
            Spacer(minLength: 0)
        }
    }

    @ViewBuilder
    private func cell(date: Date, count: Int, peak: Int, today: Date) -> some View {
        let isFuture = date > today
        let isToday = calendar.isDate(date, inSameDayAs: today)
        RoundedRectangle(cornerRadius: 4)
            .fill(isFuture ? Color.clear : count == 0 ? Color.white.opacity(0.06) : Theme.green.opacity(0.3 + 0.7 * Double(count) / Double(peak)))
            .overlay(RoundedRectangle(cornerRadius: 4).strokeBorder(isToday ? Theme.chalk.opacity(0.8) : .clear, lineWidth: 1.5))
            .frame(maxWidth: .infinity)
            .frame(height: 18)
            .help(isFuture ? "" : "\(date.formatted(.dateTime.weekday(.abbreviated).day().month(.abbreviated))) · \(count) done")
            .accessibilityLabel(isFuture ? "" : "\(date.formatted(date: .abbreviated, time: .omitted)), \(count) done")
    }

    private func taskRow(_ task: TaskDefinition, history: History, today: Date) -> some View {
        let streak = history.streak(for: task.id, today: today)
        let done = history.timesDone(task.id, inLast: 30, today: today)
        return HStack(spacing: 12) {
            TaskIcon(task.icon, size: 13)
                .frame(width: 20)
                .foregroundStyle(TaskCategory(task).color)
            VStack(alignment: .leading, spacing: 2) {
                Text(task.title)
                    .font(.system(size: 13, weight: .semibold))
                    .lineLimit(1)
                Text("\(done) of the last 30 days")
                    .font(Theme.mono())
                    .foregroundStyle(Theme.muted)
            }
            Spacer(minLength: 8)
            if streak > 0 {
                HStack(spacing: 3) {
                    Image(systemName: "flame.fill").foregroundStyle(Theme.green)
                    Text("\(streak)")
                }
                .font(Theme.mono(12).weight(.medium))
                .accessibilityLabel("\(streak) days in a row")
            }
        }
        .card()
    }
}
