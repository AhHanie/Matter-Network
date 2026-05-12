using RimWorld;

namespace SK_Matter_Network
{
    public class ITab_MatterIOPortFilter : ITab_Storage
    {
        protected override bool IsPrioritySettingVisible => false;

        public ITab_MatterIOPortFilter()
        {
            labelKey = "MN_MatterIOPortFilterTab";
            tutorTag = "MatterIOPortFilter";
        }
    }
}
