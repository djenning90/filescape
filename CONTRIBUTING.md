# Contributing to FileScape

FileScape turns your computer into places you can explore. Help us make those places useful, easy to navigate, and
delightful to spend time in.

## Play and share what you notice

Try Learn to play in Starter Sky, then use FileScape for a real task. Tell us where you got lost, what felt good, and
what you wished you could do next. A first impression, a bug report, a music idea, or a documentation improvement is a
contribution.

[Open an issue](https://github.com/djenning90/filescape/issues) with your build ID, what you expected, and what happened.
For a bug, include the steps to reproduce it and your window size or UI scale when relevant. Starter Sky is a useful
place to capture examples without exposing your personal files.

## Work on a change

For a substantial feature or redesign, open an issue to discuss the player experience before investing heavily.
Focused fixes can go straight to a pull request. David Jennings maintains the project and reviews its direction.

Fork the repository, create a branch, and follow the [Windows workflow](docs/windows-workflow.md) to run the game.
FileScape uses Godot 4.7.2 .NET and .NET 10. The [architecture](docs/architecture.md) and
[design decisions](docs/decisions.md) explain how the project fits together.

Describe what your change lets a player do, or the concrete failure it fixes. Apply the same expectation across
comparable interfaces. Explain non-obvious reasoning and name tuning values with units, following the
[code documentation conventions](docs/code-documentation.md).

Run the relevant focused tests during development. The Windows workflow lists area tests and broader checks.
Include the commands and results in your pull request, along with a short clip or screenshots for visible changes.
Coordinate real desktop input tests so they do not interfere with another person's work.

AI-assisted contributions are welcome. Review what you submit, understand its behavior, and describe how you checked it.

## Licence and community

FileScape uses the [MIT License](LICENSE). Submit work you have the right to contribute under that licence.
Treat contributors with respect, explain disagreements constructively, and credit the people whose work you build on.
