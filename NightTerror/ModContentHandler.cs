using Dusk;

namespace NightTerror
{
    internal class ModContentHandler : ContentHandler<ModContentHandler>
    {
        internal NightTerrorEnemyAssets? nightTerrorEnemyAssets;




        public class NightTerrorEnemyAssets(DuskMod mod, string filePath) : AssetBundleLoader<NightTerrorEnemyAssets>(mod, filePath)
        {

        }

        public ModContentHandler(DuskMod mod) : base(mod)
        {
            RegisterContent("nightterrordef", out nightTerrorEnemyAssets);
        }
    }
}
