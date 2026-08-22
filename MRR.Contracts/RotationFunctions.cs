using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Windows.Media;

namespace MRR
{
    public class RotationFunctions
    {

        //static public RotateTransform ImageRotation(Direction p_ImageDirection)
        //{
        //    int[] imgRot = { 0, 0, 90, 180, 270 };
        //    return new RotateTransform(imgRot[(int)p_ImageDirection]);
        //}

        static public Direction Rotate(int RotateDir, Direction StartingDirection)
        {
            int currentdir = (int)(StartingDirection);
            if (RotateDir == -2) RotateDir = 2;

            Direction[,] dirArray = {{Direction.None,Direction.None,Direction.None,Direction.None}, // none
            {Direction.Left,Direction.Up,Direction.Right,Direction.Down}, // up
            {Direction.Up,Direction.Right,Direction.Down,Direction.Left}, // right
            {Direction.Right,Direction.Down,Direction.Left,Direction.Up}, // down
            {Direction.Down,Direction.Left,Direction.Up,Direction.Right}}; // left

            return dirArray[currentdir, RotateDir + 1]; ;
        }

        static public int RotationDifference(Direction StartDirection, Direction EndDirection)
        {
            int newdirection = (int)EndDirection - (int)StartDirection;
            if (newdirection < -1) newdirection += 4;
            if (newdirection > 2) newdirection -= 4;
            return newdirection;
        }

        static public Direction SumDirections(Direction RobotDirection, Direction OptionDirection)
        {
            //int turnDifference = 
            int robot = ((int)RobotDirection + ((int)OptionDirection - 1))%4;
            if (robot == 0) robot = 4;

            return (Direction)robot;
        }

        /// <summary>
        /// This returns the direction of an optional weapon, given the robot's facing and the
        /// option's own direction. When the weapon is receiving damage from an Incoming Weapon,
        /// pass IncomingWeapon = true.
        /// </summary>
        /// <param name="p_robotFacing">The robot's current facing (was a Player; only the
        /// facing was ever read, and taking a Direction keeps this file free of Player so it
        /// can live in MRR.Contracts).</param>
        /// <param name="p_option"></param>
        /// <param name="IncomingWeapon"></param>
        static public Direction GetOptionDirection(Direction p_robotFacing, OptionCard p_option, bool IncomingWeapon = false)
        {
            Direction OptDir = SumDirections(p_robotFacing, p_option.OptionDirection);
            if (IncomingWeapon) OptDir = RotationFunctions.Rotate(2, OptDir);
            return OptDir;
        }

        static public Direction IncomingDirection(Direction DirectionIN)
        {
            return Rotate(2,DirectionIN);
        }

        static public (int X, int Y) MovementOffset(Direction FacingDirection) => FacingDirection switch
        {
            Direction.Up    => ( 0, -1),
            Direction.Down  => ( 0,  1),
            Direction.Right => ( 1,  0),
            Direction.Left  => (-1,  0),
            _               => ( 0,  0),
        };

        static public int Degrees(int FacingDirection)
        {
            switch ((Direction)FacingDirection)
            {
                case Direction.Up:
                    return 0;
                case Direction.Right:
                    return 90;
                case Direction.Down:
                    return 180;
                case Direction.Left:
                    return 270;
                case Direction.None:
                default:
                    return 0;
            }
        }

    }
}
