using System.Collections.Generic;

namespace ChristmasInDirtmouth
{
    // https://prashantmohta.github.io/ModdingDocs/saving-mod-data.html
    public class ModData
    {
        public bool ShopIntro = true;

        // Active Inventory
        public bool[] HeroInventory = new bool[] { false, false, false, false, false };

        // Diálogos navideños ya vistos por cada NPC para no repetirlos
        public List<string> SeenChristmasDialogues = new List<string>();

        public void Reset()
        {
            ShopIntro = true;
            HeroInventory = new bool[] { false, false, false, false, false };
            SeenChristmasDialogues = new List<string>();
        }
    }
}
