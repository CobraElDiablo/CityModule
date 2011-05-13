using OpenMetaverse;
using OpenSim.Region.Framework.Scenes;
using Aurora.Framework;

using Nini;
using Nini.Config;

namespace Aurora.Framework
{
    public class ICityBuilding
    {
        private string m_BuildingName = string.Empty;

        public string BuildingName
        {
            get { return (m_BuildingName); }
            set { m_BuildingName = value; }
        }
    }
}
