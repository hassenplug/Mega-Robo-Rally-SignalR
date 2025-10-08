using System;

namespace MRR_CLG
{
    public class PhaseFunctions
    {
        static public bool GetActive(int PhaseBitmap, int SelectedPhase)
        {
            if (SelectedPhase == 6) SelectedPhase = 1;
            int calcphase = ConvertPhaseToPhaseBitmap(SelectedPhase);
            return ((PhaseBitmap & calcphase) != 0);

        }

        static public int SetActive(int CurrentPhaseBitmap, int Phase, bool Setting)
        {
            //int digit = Setting ? 1 : 0;
            if (Phase == 6)
            {
                return Setting ? 31 : 0;
            }
            else
            {
                int calcphase = (int)Math.Pow(2, Phase - 1);
                if (Setting)
                {
                    CurrentPhaseBitmap = CurrentPhaseBitmap | calcphase;
                }
                else
                {
                    //Current = Current | ~calcphase;
                    CurrentPhaseBitmap = CurrentPhaseBitmap & (31 - calcphase);
                }
                return CurrentPhaseBitmap;

            }

        }

        static public int ConvertPhaseToPhaseBitmap(int ActualPhase)
        {
            return (int)Math.Pow(2, ActualPhase - 1);

        }
    }
}
