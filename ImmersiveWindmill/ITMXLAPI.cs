using Microsoft.Xna.Framework;
using StardewValley;

namespace ImmersiveWindmill
{
	public interface ITMXLAPI
	{
		string BuildBuildable(string id, GameLocation location, Point position);

		void RemoveBuildable(string uniqueid);

		void MoveBuildable(string uniqueid, GameLocation location, Point position);
	}
}
