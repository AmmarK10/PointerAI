namespace PointerAI.Services;
public sealed record ScreenAssistantResult(string Answer, bool TargetFound, double X, double Y, double Width, double Height, double Confidence);