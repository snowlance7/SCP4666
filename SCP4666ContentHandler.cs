using Dusk;

namespace SCP4666
{
    public class SCP4666ContentHandler : ContentHandler<SCP4666ContentHandler>
    {
        public class SCP4666Assets(DuskMod mod, string filePath) : AssetBundleLoader<SCP4666Assets>(mod, filePath) { }

        public SCP4666Assets? SCP4666;

        public SCP4666ContentHandler(DuskMod mod) : base(mod)
        {
            RegisterContent("scp4666_assets", out SCP4666);
        }
    }
}