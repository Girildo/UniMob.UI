using System.Runtime.CompilerServices;

// The window's own types are internal, and should stay that way: nothing outside this assembly has
// any business building a widget-tree snapshot. Their hit testing is the part most worth pinning
// down, though -- it decides what a click selects, it has already been wrong in three different ways,
// and every one of those was a rule about overlapping boxes that reads as correct until you run it.
[assembly: InternalsVisibleTo("UniMob.UI.Tests")]
