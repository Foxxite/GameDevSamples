
using System;

namespace SpaceDefence
{
    [Flags]
    public enum CollisionType : int
    {
        None = 0,
        Team1 = 1,
        Team2 = 2,
        Teams = Team1 | Team2,
        Solid = 4,
    }
}