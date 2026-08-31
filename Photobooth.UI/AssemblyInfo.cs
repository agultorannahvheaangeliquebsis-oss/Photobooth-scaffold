using System.Runtime.CompilerServices;

// Lets Photobooth.UI.Tests assert internal members directly (e.g.
// KioskViewModel.MapScreen) without exposing them as public API just for
// testability.
[assembly: InternalsVisibleTo("Photobooth.UI.Tests")]
