using System;

namespace Chosen.Sim;

/// <summary>
/// An AI state (Blood's AISTATE): the animation it plays (seq offset from the dude's first sequence, with its frame
/// callback), how long it lasts, and what it does on entering, every frame (move) and every think.
/// </summary>
public sealed class AiState
{
    public const int Other = -1, Idle = 0, GenIdle = 1, Move = 2, Search = 3, Chase = 4, Recoil = 5, Attack = 6,
        Knockout = 7, IdleSleep = 8;

    public readonly string Name;
    public int Type, Seq = -1, Ticks;
    public Action<Sprite> SeqCallback, Enter, Think;
    public Action<Sprite> MoveFn;
    public AiState Next;
    /// <summary>A custom dude's state: which of its states and postures this is (-1: no custom dude's).</summary>
    public int CdState = -1, CdPosture;

    public AiState(string name) => Name = name;

    public void Set(int type, int seq, int ticks)
    {
        Type = type;
        Seq = seq;
        Ticks = ticks;
    }


    public override string ToString() => Name;
}
