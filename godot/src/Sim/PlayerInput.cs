namespace Chosen.Sim;

/// <summary>
/// One player's input for one Blood frame: everything the simulation reads from a player. Kept as plain data so a
/// frame can be recorded, replayed or (later) sent over the network.
/// </summary>
public struct PlayerInput
{
    public float Forward;        // Blood speed units: 2048 run, 1024 walk, negative backwards
    public float Strafe;         // positive left (Blood's svel; QC: inputStrafe = -movement_y)
    public float Up;
    public bool Fire, AltFire, Jump, Crouch, Use;
    public int Impulse;          // weapon / inventory selection (Blood3D's impulse numbers)
    public float Pitch, Yaw;     // view angles in degrees (Quake convention: pitch down positive, yaw counter-clockwise)
}
