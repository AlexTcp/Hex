// =============================================================================
// TokenCatalog
// =============================================================================
// Purpose:
//   Static registry that exposes a read-only list of factory delegates, one
//   per concrete Token subclass shipped with the game. Used by token
//   selection UI and game setup to enumerate available pieces and lazily
//   instantiate them by index.
//
// Interactions:
//   - Token: All factories return a Token instance.
//   - Walker, Charger, Stepper, Skipper, Runner, Jumper, Diamond, Knight,
//     Spiral, Drifter, Orbit, Ringwalk, Anchor, Shrine, Edge, Mirror, Echo,
//     Pivot: each concrete Token subclass (defined in Tokens.cs) is
//     constructed by one of the catalog's factory delegates.
// =============================================================================

using System;
using System.Collections.Generic;

namespace HexGame.Tokens;

public static class TokenCatalog
{
    public static readonly IReadOnlyList<Func<Token>> All = new Func<Token>[]
    {
        () => new Walker(),
        () => new Charger(),
        () => new Stepper(),
        () => new Skipper(),
        () => new Runner(),
        () => new Jumper(),
        () => new Diamond(),
        () => new Knight(),
        () => new Spiral(),
        () => new Drifter(),
        () => new Orbit(),
        () => new Ringwalk(),
        () => new Anchor(),
        () => new Shrine(),
        () => new Edge(),
        () => new Mirror(),
        () => new Echo(),
        () => new Pivot(),
    };
}
