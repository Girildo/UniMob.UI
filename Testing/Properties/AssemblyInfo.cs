using System.Runtime.CompilerServices;

// The package's own fixtures use helpers an app has no business with: a state that throws on every
// member, and the drivers that reproduce a frame's two layout pulls.
[assembly: InternalsVisibleTo("UniMob.UI.Tests")]
