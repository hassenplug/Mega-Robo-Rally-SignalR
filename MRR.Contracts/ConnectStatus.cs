namespace MRR
{
    /// <summary>
    /// Robots.ConnectStatusID — whether we have a live WebSocket to the robot, distinct from
    /// tPlayerStatus (Robots.Status), which is the robot's *game* state. Values are RobotStatusID
    /// rows in the RobotStatus table (see install/MRRDatabase.sql, install/todo.md Section 9).
    /// IDs 20-23 avoid the 0-14 range already used by tPlayerStatus; Unknown reuses 0.
    /// </summary>
    public enum tConnectStatus
    {
        [StatusInfo("FFFFFF", "FFFFFF", "Unknown")]     Unknown      = 0,
        [StatusInfo("FF0000", "FF0000", "Not Conn")]    NotConnected = 20,
        [StatusInfo("FFFF00", "FFFF00", "Connecting")]  Connecting   = 21,
        [StatusInfo("00FF00", "00FF00", "Connected")]   Connected    = 22,
        [StatusInfo("800080", "800080", "Searching")]   Searching    = 23,
    }
}
