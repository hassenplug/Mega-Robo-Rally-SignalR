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

namespace MRR
{
    public class OptionCardList : List<OptionCard>
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

        public void ClearFromPlayer(OptionCard removeCard, PlayerState? fromPlayer)
        {
            //RRGame maingame = fromPlayer.MainGame;
            // need to clear from whole list, not from player list..
            //maingame.OptionCards.Remove(removeCard);
            //maingame.ListOfCommands.AddCommand(fromPlayer, SquareAction.DestroyOptionCard, removeCard.ID);

        }

        public OptionCard? GetOption(tOptionCardCommandType OptionID, PlayerState usePlayer, int Phase = -1)
        {
            //OptionCard useCard = this.FirstOrDefault(uc => uc.ID == (int)OptionID);  // return that card
            OptionCard? useCard = this.FirstOrDefault(uc => (uc.ID == (int)OptionID) && (uc.Owner==usePlayer.ID));  // return that card

            if (useCard == null) return null; // option not available for this player

            if (Phase == -1) return useCard;

            if (!useCard.IsActive(Phase) ) return null;

            return useCard;
        }

        public OptionCard? GetOption(int OptionID)
        {
            OptionCard? useCard = this.FirstOrDefault(uc => uc.ID == OptionID);
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

        public OptionCard? GetOptionToDestroy(PlayerState Player)
        {
            OptionCard? useCard = this.FirstOrDefault(uc => uc.Owner == Player.ID && uc.DestroyWhenDamaged);
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
                //this.MoveItem(currentIndex, currentIndex + MoveDirection);
            }
        }

    }
}
