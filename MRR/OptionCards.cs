using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel;
//using System.Threading;
using System.ComponentModel;

using System.Windows;
//using System.Windows.Controls;
//using System.Windows.Media;
using System.Xml.Serialization; // serializer
//using System.Windows.Controls;
//using System.Windows.Data; // border
//using System.Windows.Media.Imaging;

namespace MRR_CLG
{
    public class OptionCardList : ObservableCollection<OptionCard>
    {

        public OptionCardList()
            : base()
        {
            // set of cards
            //BuildDictionary();
        }

        public OptionCardList(IEnumerable<OptionCard> ExistingList)
            : this()
        {
            foreach (OptionCard thiscard in ExistingList)
            {
                this.Add(thiscard);
            }
        }

        public void ClearFromPlayer(OptionCard removeCard, Player fromPlayer)
        {
            //RRGame maingame = fromPlayer.MainGame;
            // need to clear from whole list, not from player list..
            //maingame.OptionCards.Remove(removeCard);
            //maingame.ListOfCommands.AddCommand(fromPlayer, SquareAction.DestroyOptionCard, removeCard.ID);

        }

        public OptionCard GetOption(tOptionCardCommandType OptionID, Player usePlayer, int Phase = -1)
        {
            //OptionCard useCard = this.FirstOrDefault(uc => uc.ID == (int)OptionID);  // return that card
            OptionCard useCard = this.FirstOrDefault(uc => (uc.ID == (int)OptionID) && (uc.Owner==usePlayer.ID));  // return that card

            if (useCard == null) return null; // option not available for this player

            if (Phase == -1) return useCard;

            if (!useCard.IsActive(Phase) ) return null;

            return useCard;
        }

        public OptionCard GetOption(int OptionID)
        {
            OptionCard useCard = this.FirstOrDefault(uc => uc.ID == OptionID);
            //OptionCard useCard = this[OptionID];
            return useCard;
        }

        public OptionCardList GetOptions(tOptionCardCommandType OptionID, int Phase = -1)
        {
            if (Phase == -1)
            {
                return new OptionCardList(this.Where(oc => oc.ID == (int)OptionID));
            }
            else
            {
                // return cards that are active this phase for this player
                return new OptionCardList(this.Where(oc => (oc.ID == (int)OptionID) && (oc.IsActive(Phase))));
            }
        }

        public void AddOptionsToList(OptionCardList NewList)
        {
            foreach (OptionCard newcard in NewList)
            {
                this.Add(newcard);
            }
        }

        public OptionCard GetOptionToDestroy(Player Player)
        {
            OptionCard useCard = this.FirstOrDefault(uc => uc.Owner == Player.ID && uc.DestroyWhenDamaged);
            //OptionCard useCard = this[OptionID];
            return useCard;
        }

        /// <summary>
        /// Move this option on the option list
        /// </summary>
        /// <param name="thisOption"></param>
        /// <param name="MoveDirection"></param>
        public void MoveOption(OptionCard thisOption, int MoveDirection)
        {
            if (thisOption != null)
            {
                int currentIndex = this.IndexOf(thisOption);
                this.MoveItem(currentIndex, currentIndex + MoveDirection);
            }
        }

    }

    public enum tOptionCardCommandType
    {
        Undefined = -1,
        Random = 0, // option to be selected...
        AblativePaint = 1,
        Brakes = 6,
        BridgeLayer = 7, //
        CircuitBreaker = 9,
        DoubleBarrelLaser = 13,
        ExtraMemory = 16,
        FlyWheel = 18,
        FourthGear = 19,
        GyroscopicStabilizer = 22,
        HighPowerLaser = 23,
        MineLayer = 27, //
        PowerDownShield = 33,
        RammingGear = 37,
        RearLaser = 38,
        Recompile = 39,
        Reflector = 41,
        ReverseGears = 43,
        ScramblerBomb = 46,
        SelfDestruct = 47,
        Shield = 48,
        SuperiorArchive = 49,
        TheBigOne = 50,
        Turret = 52,
        ExplosiveLaser = 53,
        //BonusMemory = 54,
        PointSucker = 55,
        EMP = 56,
        DamageEraser = 57,
        Reboot = 58,
        AdditionalLaser = 59


    }

    public enum tOptionEditorType
    {
        Undefined = 0,
        Automatic = 1,      // always active
        PhaseSelector = 2,  // Phase(es) to activate
        SinglePhase = 3,    // Select one phase
        TurnSelector = 4,   // on/off per turn
        CardToSave = 5,     // select one card after programmed (flywheel)
        Direction = 6,      // direction of option (including not used?)
        OverloadPhase = 7,  // Place two cards in one phase
        OptionalWeapon = 8, // This weapon is on or off for [phase/turn]?
        Runtime = 9,        // not implemented
    }

    /*
     * option notes
     * Need to disable gyro and other options
     * need to set distance for high powered laser
     * Need method to set turret direction
     * rotate shield direction and reflector direction
     * ScramblerBomb not complete
     */


    public class OptionCard
    {
        public OptionCard()
        {
            Source = tOptionSource.BaseGame;
            Type = tOptionType.Unknown;
            Damage = 0;
            Kind = new List<tOptionKind>();
            EditorType = tOptionEditorType.Automatic;
            //IsEnabled = false;
            Quantity = -1; // unlimited
        }

        public OptionCard(OptionCard copyCard):this()
        {
            if (copyCard==null) return ;
            ID = copyCard.ID;
            Name = copyCard.Name;
            Text = copyCard.Text;
            SRR_Text = copyCard.SRR_Text;
            EditorType = copyCard.EditorType;
            Damage = copyCard.Damage;
            DataValue = copyCard.DataValue;
            DestroyWhenDamaged = copyCard.DestroyWhenDamaged;

            Quantity = copyCard.Quantity;

            ActionSequence = copyCard.ActionSequence;

            //Owner = copyCard.Owner;
            Source = copyCard.Source;
            Type = copyCard.Type;
            Kind = copyCard.Kind;
            FunctionalStatus = copyCard.FunctionalStatus;
            CurrentOrder = copyCard.CurrentOrder;

        }

        public OptionCard(int p_RobotID, tOptionCardCommandType p_ID, int p_Destroy, int p_Quantity, int p_IsActive, int p_PhasePlayed, int p_DataValue, int p_Damage, string p_Name ): this()
        {
            Owner = p_RobotID;
            ID = (int)p_ID;
            Damage = p_Damage;
            //EditorType = tOptionEditorType.Automatic;
            Quantity = p_Quantity;
            DataValue = p_DataValue;
            DestroyWhenDamaged = (p_Destroy == 1);
            PhasePlayed = p_PhasePlayed;
            //IsActive = p_IsActive;
            Name = p_Name;
        }

        public enum tOptionSource
        {
            BaseGame,
            ArmedAndDangerous
        }

        public enum tOptionType
        {
            Automatic,              // no user input required
            RunTime,                // Activate During Run-time
            OptionalWeapon,         // Activate During Run-time (currently turn programmed)
            PhaseProgrammedMovement,// Activate selected phase
            PhaseProgrammedGadget,  // Activate selected phase
            PhaseProgrammed,        // Activate selected phase
            TurnProgrammed,         // Activate each turn
            MainLaserModification,  // No input required?  (activate)
            AdditionalWeapon,       // ??
            Unknown
        }

        public enum tOptionKind
        {
            Flying,
            Booster,
            Launcher,
            FlyingDevice,
            FlatDevice,
            Device
        }

        public enum tFunctionalStatus
        {
            [Description("No")]NotGoingToAddress,
            [Description("Yes")]GoingToAddress,
            [Description("No")]Addressing,
            [Description("No")]UnderConstruction,
            [Description("No")]Phase1,
            [Description("No")]Phase2,
            [Description("No")]Phase3,
            [Description("No")]Phase4,
            [Description("No")]Untested,
            [Description("No")]Functional
        }

        /*
Type:Run Time
Type:Optional Weapon
Type:Phase Programmed Movement
Type:Phase Programmed Gadget
Type:Turn Programmed
Type:Main Laser Modification
Type:Additional Weapon
         
Type:Optional Weapon(1)/Turn Programmed(2)

         * Kind:Launcher
Kind:Flying, Booster
Kind:Flat Device, Launcher
Kind:Flying Device, Launcher
Kind:Flying Device, Launcher
Kind:Flat Device, Launcher
Kind:Flat Device, Launcher
Kind:Flying Device, Launcher
Kind:Flat Device, Launcher
Kind:Flat Device, Launcher
Kind:Flying, Booster
Kind:Flying
Kind:Flat Device, Launcher
Kind:Device, Launcher

    */

        /*
         *
         * Options need to include:
         * Phase/Turn Programmed or Always enabled
         * Directional (none/normal)
         *
         * */


        //public void ResetCard()
        //{
        //    Owner = -1;  // unowned
        //    PhasePlayed = -1; // unplayed
        //    //Random = false;
        //    CurrentOrder += 100;
                
        //    // reset quantity
        //    //QuantityRemaining = Quantity;
        //}

        //public void CopySettings(OptionCard otherCard)
        //{
        //    Owner = otherCard.Owner;
        //    DataValue = otherCard.DataValue;
        //    DestroyWhenDamaged = otherCard.DestroyWhenDamaged;
        //    //QuantityRemaining = otherCard.QuantityRemaining;
        //}

        public int ID { get; set; }

        [XmlIgnore]
        public tOptionCardCommandType CommandType { get { return (tOptionCardCommandType)ID; } }
        //public tOptionCardCommandType CommandType { get; set; }

        //[XmlIgnore]
        //public int Priority { get { return ID * 10; } }

        //[XmlIgnore]
        //public bool CardSelected { get { return (PhasePlayed > -1); } }

        //[XmlIgnore]
        //public tCardType Type { get; set; }
        //public string ShortText { get; set; }
        //[XmlIgnore]
        //public string Text { get { return DescriptionFunctions.GetDescription(Type); } }
        public string Name { get; set; }

        public string Text { get; set; }
        public string SRR_Text { get; set; }

        public tOptionSource Source { get; set; } // this is not really used.  It's where the option came from
        public tOptionType Type { get; set; }
        public tOptionEditorType EditorType { get; set; }

        public bool DestroyWhenDamaged { get; set; }

        public int Damage { get; set; }

        public int Quantity { get; set; }

        public List<tOptionKind> Kind { get; set; }

        public int ActionSequence { get; set; }

        public tFunctionalStatus FunctionalStatus { get; set; }

        public bool IsActive(int Phase = 0)
        {
            if (!IsOwned) return false;
            if (this.EditorType == tOptionEditorType.Automatic) return true;
            if (this.EditorType == tOptionEditorType.CardToSave) return true;
            if (Quantity == 0) return false;
            return PhaseFunctions.GetActive(PhasePlayed, Phase);
        }

        public bool Use(int useQuantity = 1)
        {
            if (Quantity == 0) return false;
            if (Quantity == -1) return true;
            if (Quantity > 0)
            {
                Quantity -= useQuantity;
                return true;
            }

            return false;
        }

        public bool IsOwned { get { return Owner > 0; } }

        public int Owner { get; set; }

        public int PhasePlayed { get; set; }

        [XmlIgnore]
        public Direction OptionDirection  // some options require a direction
        {
            get
            {
                if (EditorType == tOptionEditorType.Direction)
                {
                    return (Direction)DataValue;
                }
                else
                {
                    return Direction.None;
                }
                //return l_optionDirection;
            }
            set
            {
                DataValue = (int)value;
            }
        }

        public int CurrentOrder { get; set; } // random selection of cards

        public int DataValue { get; set; }

        public override string ToString()
        {
            string output = Name; // +":" + Text;
            //string output = "(" + ID.ToString() + ") ";
            //if (Owner > -1) output += " Player:" + Owner.ToString();
            //if (PhasePlayed > 0) output += " Phase:" + PhasePlayed.ToString();
            //if (Locked) output += " [Locked]";

            return output;
        }

    }

}
