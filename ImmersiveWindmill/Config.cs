using System.Collections.Generic;

namespace ImmersiveWindmill
{
	public class Config
	{
		public bool MillsHaveInteriors { get; set; } = true;
		public bool MillsMakeSounds { get; set; } = true;
		public Dictionary<string, string> MillableItems { get; set; } = new Dictionary<string, string>
		{
			{ "262", "246" }, // Wheat => Wheat Flour
			{ "271", "423" }, // Unmilled Rice => Rice
			{ "284", "245" }, // Beet => Sugar
		};
		public bool CheckAllLocationsForMills { get; set; } = false;
		public bool DebugMode { get; set; } = true;
	}
}
