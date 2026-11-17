// Chosen core: original code.
namespace Chosen.Shared.Engine.Geometry;

/// <summary>
/// A sector of a level made of sectors and wall loops: its walls are WallNum consecutive walls from WallPtr, forming
/// one outer loop and any inner loops. Heights are in z units (16 to an xy unit, growing downwards); a sloped plane
/// rises Heinum / 4096 z units per z unit of horizontal distance from the line through the sector's first wall.
/// </summary>
public interface IGeomSector
{
    int WallPtr { get; }
    int WallNum { get; }
    int CeilingZ { get; }
    int FloorZ { get; }
    /// <summary>Bit 2 (value 2) set: the plane is sloped by CeilingHeinum.</summary>
    int CeilingStat { get; }
    int FloorStat { get; }
    int CeilingHeinum { get; }
    int FloorHeinum { get; }
}

/// <summary>A wall: from (X, Y) to the start of wall Point2 (the next in its loop); NextWall / NextSector the wall
/// and sector on its other side (-1: a solid wall).</summary>
public interface IGeomWall
{
    int X { get; }
    int Y { get; }
    int Point2 { get; }
    int NextWall { get; }
    int NextSector { get; }
}

/// <summary>A level's sectors and walls, read through the interfaces above.</summary>
public interface IBuildData
{
    int SectorCount { get; }
    int WallCount { get; }
    IGeomSector Sector(int i);
    IGeomWall Wall(int i);
}
