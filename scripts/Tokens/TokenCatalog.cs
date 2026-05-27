// =============================================================================
// TokenCatalog
// =============================================================================
// Purpose:
//   Static registry that exposes a read-only array of TokenInfo records, one
//   per concrete Token subclass shipped with the game. Each entry pairs the
//   token's display name and description (read by the picker UI without
//   allocating a Token) with a factory delegate used to instantiate the
//   piece on demand.
//
// Interactions:
//   - Token: All factories return a Token instance.
//   - Walker, Charger, Stepper, Skipper, Runner, Hopper, Jumper, Halo,
//     Diamond, Glider, Knight, Camel, Spiral, Drifter: each concrete Token
//     subclass (defined in Tokens.cs) is constructed by one of the catalog's
//     factory delegates.
// =============================================================================

using System;

namespace HexGame.Tokens;

public readonly record struct TokenInfo(string Name, string Description, Func<Token> Factory);

public static class TokenCatalog
{
    public static readonly TokenInfo[] All = new[]
    {
        new TokenInfo("Walker",   "Steps to any adjacent hex.",                                   () => (Token)new Walker()),
        new TokenInfo("Charger",  "Charges exactly 2 hexes in a straight line.",                  () => (Token)new Charger()),
        new TokenInfo("Stepper",  "Lands exactly 3 hexes away in a straight line.",               () => (Token)new Stepper()),
        new TokenInfo("Skipper",  "Lands exactly 4 hexes away in a straight line.",               () => (Token)new Skipper()),
        new TokenInfo("Runner",   "Slides any number of hexes in one of six directions.",         () => (Token)new Runner()),
        new TokenInfo("Hopper",   "Steps to an adjacent hex or one exactly 3 away (skips 2).",    () => (Token)new Hopper()),
        new TokenInfo("Jumper",   "Teleports to any hex exactly 2 away.",                         () => (Token)new Jumper()),
        new TokenInfo("Halo",     "Teleports to any hex exactly 3 away.",                         () => (Token)new Halo()),
        new TokenInfo("Diamond",  "Slips to one of six diagonal hexes between directions.",       () => (Token)new Diamond()),
        new TokenInfo("Glider",   "Slides any distance along one of six diagonals.",              () => (Token)new Glider()),
        new TokenInfo("Knight",   "Hex-L: 2 in one direction, 1 in an adjacent direction.",       () => (Token)new Knight()),
        new TokenInfo("Camel",    "Hex-L: 3 in one direction, 1 in an adjacent direction.",       () => (Token)new Camel()),
        new TokenInfo("Spiral",   "Any hex within distance 2.",                                   () => (Token)new Spiral()),
        new TokenInfo("Drifter",  "Drifts to any hex within distance 3.",                         () => (Token)new Drifter()),
    };
}
