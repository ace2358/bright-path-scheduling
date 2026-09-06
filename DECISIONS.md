# Decisions

## Chosen feature

Lesson-aware scheduling and conflict detection is prioritised because a bad booking immediately affects a student, tutor, room, and receptionist workflow.

## Model

`Lesson` is the scheduling unit. A lesson has one tutor and room, while `LessonParticipant` lets a lesson have one or more students. This represents an explicit shared exam lesson without confusing it with two conflicting lessons.

## Historical data

Historical CSV rows are imported as a record of what happened, including rule violations. Only rows explicitly marked `exam pair - half price` and matching all lesson details are grouped into one shared lesson. New and rescheduled lessons will be validated separately.

## Deferred next step

Implement conflict rules for new or updated lessons.
