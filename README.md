# Pointer AI

A lightweight Windows desktop companion that helps you find things on your screen. Press a hotkey, ask where something is (e.g. "where do I unsubscribe from newsletters in Gmail"), and Pointer AI analyzes a screenshot to point you toward it.

## What it does

- Sits as a small always-on-top character in the corner of your screen
- Press a hotkey to open a chat overlay
- Ask a question about anything currently visible on your screen
- Takes a one-time screenshot and sends it, along with your question, to Google's Gemini API for analysis
- Get a text answer describing where the thing you're looking for is located

## Tech stack

- **C# / .NET MAUI** — cross-platform UI framework (Windows-first; overlay window, hotkey, and screenshot capture are currently implemented for Windows only)
- **Gemini API** (`generativelanguage.googleapis.com`) — vision-capable model used to analyze screenshots and answer questions about on-screen content

## Setup

### Prerequisites
- .NET 8 SDK
- A Gemini API key (get one at [aistudio.google.com/apikey](https://aistudio.google.com/apikey))

### Configuration

Set your API key as an environment variable before running:

```
setx GEMINI_API_KEY "your-key-here"
```

Optionally override the default model:

```
setx GEMINI_MODEL "gemini-3.1-flash-lite"
```

Restart your terminal/IDE after setting environment variables for them to take effect.

### Build and run

```bash
cd PointerAI
dotnet build PointerAI.csproj
dotnet run
```

## Project status

This is an early-stage prototype built as a learning project. Current scope:

- ✅ Always-on-top transparent overlay window
- ✅ Hotkey-triggered chat interface
- ✅ On-demand screenshot capture + Gemini vision analysis
- ✅ Custom character sprite
- ⬜ Character movement/pointing toward on-screen targets
- ⬜ macOS support
- ⬜ Continuous scanning / multi-step guidance (intentionally out of scope for now)

## Notes

- Screenshots are only captured when you actively ask a question — nothing is captured or sent passively.
- Gemini's available model names change periodically; if you hit a "model no longer available" error, check [Google's current model list](https://ai.google.dev/gemini-api/docs/models) and update the `GEMINI_MODEL` environment variable or the default in `GeminiScreenAssistant.cs`.