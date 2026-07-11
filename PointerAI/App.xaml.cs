namespace PointerAI;

public partial class App : Application
{
    private readonly MainPage mainPage;

    public App(MainPage mainPage)
    {
        InitializeComponent();
        this.mainPage = mainPage;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(mainPage)
        {
            Width = 150,
            Height = 150,
            MinimumWidth = 150,
            MinimumHeight = 150,
            MaximumWidth = 1100,
            MaximumHeight = 720
        };
    }
}