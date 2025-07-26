using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using System.Numerics;

using CSVector = CounterStrikeSharp.API.Modules.Utils.Vector;
using CSQAngle = CounterStrikeSharp.API.Modules.Utils.QAngle;

namespace GameflowControl;

public partial class GameflowControl
{

    private string BannerURL = "https://3.files.edl.io/0259/22/10/02/221229-460caf3d-0a3b-48db-947d-bef5d75c2e98.png"; // Зурагны URL
    // Шалгалттай ажилладаг model (Dust2 дээр байдаг prop)
    private string BannerModel = "models/props/de_dust/hr_dust/dust_props_crates/crate_42_64.mdl";
    private CSVector BannerPosition = new CSVector(-1266, -264, 192);
    private CSVector BannerVelocity = new CSVector(0, 0, 0);
    private CSQAngle BannerAngles = new CSQAngle(0, 0, 0);

    private void SpawnBanner()
    {
        try
        {
            // Model урьдчилж ачааллах
            Server.PrecacheModel(BannerModel);

            var banner = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");
            if (banner == null)
            {
                Console.WriteLine("[MapBanner] Entity spawn амжилтгүй.");
                return;
            }

            // Model тохируулах
            banner.SetModel(BannerModel);

            // Collision унтраах (тоглогчид тээглэхгүй)
            banner.Collision.CollisionGroup = 11; // COLLISION_GROUP_DEBRIS
            banner.Collision.SolidType = SolidType_t.SOLID_NONE;

            // Байрлуулалт
            banner.Teleport(BannerPosition, BannerAngles, BannerVelocity);
            banner.DispatchSpawn();

            Console.WriteLine("[MapBanner] Banner амжилттай spawn хийлээ.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapBanner] Алдаа: {ex}");
        }
    }
}
