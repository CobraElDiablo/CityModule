using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.Reflection;

using OpenMetaverse;

using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using Aurora.Framework;

using Nini;
using Nini.Config;

using Aurora.Modules.CityBuilder;

namespace Aurora.Framework
{
    [Serializable]
    public class ICityBuilding : SceneObjectGroup
    {
        #region ICityBuilding Internal Members
        //  Data properties for this instance of a building.
        private string buildingName = string.Empty;
        private BuildingType buildingType = BuildingType.BUILDING_GENERAL;
        private BuildingFlags buildingFlags = BuildingFlags.BUILDING_FLAG_NONE;
        private Vector3 buildingCenter = Vector3.Zero;
        private BuildingPlot buildingPlot = new BuildingPlot();
        private int buildingHeight = CityModule.randomValue(10);  // building height.
        private List<UUID> buildingTextures = new List<UUID>();
        private int buildingSeed = CityModule.randomValue(65355);
        private int buildingRoofTiers = 1;
        private Vector4 buildingColour = Vector4.Zero;
        private Vector4 buildingTrimColour = Vector4.Zero;
        private UUID buildingGUID = UUID.Zero;  //  Unique to either a group (complex) or single building.
        private UUID buildingUUID = UUID.Zero;  //  Unique for this building
        private UUID buildingOwner = UUID.Zero; // Should this be the same as the SceneObjectGroup 'owner'?
        [XmlIgnore]
        private Scene scene = null; // Which scene or region this building belongs too, needed to primitive manipulation.
        #endregion
        #region ICityBuilding Public Properties

        public string BuildingName
        {
            get { return buildingName; }
            set { buildingName = value; }
        }

        public BuildingType BuildingType
        {
            get { return (buildingType); }
            set { buildingType = value; }
        }

        public BuildingFlags BuildingFlags
        {
            get { return (BuildingFlags); }
            set { BuildingFlags = value; }
        }

        public Vector3 BuildingCenter 
        {
            get { return (buildingCenter); }
            set { buildingCenter = value; }
        }

        public BuildingPlot BuildingPlot
        {
            get { return (buildingPlot); }
            set { buildingPlot = value; }
        }

        public int BuildingHeight
        {
            get { return (buildingHeight); }
            set { buildingHeight = value; }
        }

        public List<UUID> BuildingTextures
        {
            get { return (buildingTextures); }
            set { buildingTextures = value; }
        }

        public int BuildingSeed
        {
            get { return (buildingSeed); }
            set { buildingSeed = value; }
        }

        public int BuildingRoofTiers
        {
            get { return (buildingRoofTiers); }
            set { buildingRoofTiers = value; }
        }

        public Vector4 BuildingColour
        {
            get { return (buildingColour); }
            set { buildingColour = value; }
        }

        public Vector4 BuildingTrimColour
        {
            get { return (buildingTrimColour); }
            set { buildingTrimColour = value; }
        }

        public UUID BuildingGUID
        {
            get { return (buildingGUID); }
            set { buildingGUID = value; }
        }

        public UUID BuildingUUID
        {
            get { return (buildingUUID); }
            set { buildingUUID = value; }
        }

        public UUID BuildingOwner
        {
            get { return (buildingOwner); }
            set { buildingOwner = value; }
        }

        #endregion
        #region Constructors

        public ICityBuilding():base(null)
        {
        }

        /// <summary>
        /// Construct the building class instance from the given properties.
        /// </summary>
        /// <param name="type" type="BuildingType">type</param>
        /// <param name="plot">The plot of land this building stands on, note it might be bigger than the
        /// actual buildings footprint, for example if it is part of a larger complex, limit the size of
        /// buildings to have a footprint of no more than 100 square meters.</param>
        /// <param name="flags"></param>
        /// <param name="owner">The owner of the building either a user, or company (group of companies) own buildings.</param>
        /// <param name="seed"></param>
        /// <param name="height">The height in floors of the building, not each floor is approximately 3 meters in size
        /// and thus buildings are limited to a maximum height of 100 floors.</param>
        public ICityBuilding( BuildingType type, BuildingPlot plot, BuildingFlags flags, 
            UUID owner, IScene scene, string name ):base(owner,new Vector3(plot.XPos,21,plot.YPos),
            Quaternion.Identity, PrimitiveBaseShape.CreateBox(), name, scene)
        {
            //  Start the process of constructing a building given the parameters specified. For
            // truly random buildings change the following value (6) too another number, this is
            // used to allow for the buildings to be fairly fixed during research and development.
            BuildingSeed = 6; // TODO FIX ACCESS TO THE CityModule.randomValue(n) code.
            BuildingType = type;
            BuildingPlot = plot;
            BuildingFlags = flags;
            //  Has a valid owner been specified, if not use the default library owner (i think) of the zero uuid.
            if (!owner.Equals(UUID.Zero))
                BuildingOwner = owner;
            else
                BuildingOwner = UUID.Zero;

            //  Generate a unique value for this building and it's own group if it's part of a complex,
            // otherwise use the zero uuid for group (perhaps it should inherit from the city?)
            BuildingUUID = UUID.Random();
            BuildingGUID = UUID.Random();

            BuildingCenter = new Vector3((plot.XPos + plot.Width / 2), 21, (plot.YPos + plot.Depth) / 2);
            if (name.Length > 0)
                BuildingName = name;
            else
                BuildingName = "Building" + type.ToString();
            //  Now that internal variables that are used by other methods have been set construct
            // the building based on the type, plot, flags and seed given in the parameters.
            switch (type)
            {
                case BuildingType.BUILDING_GENERAL:
                    OpenSim.Framework.MainConsole.Instance.Output("Building Type GENERAL", log4net.Core.Level.Info);
                    //createBlocky();
                    break;
                case BuildingType.BUILDING_LOCALE:
                    /*
                    switch ( CityModule.randomValue(8) )
                    {
                        case 0:
                            OpenSim.Framework.MainConsole.Instance.Output("Locale general.", log4net.Core.Level.Info);
                            createSimple();
                            break;
                        case 1:
                            OpenSim.Framework.MainConsole.Instance.Output("locale 1", log4net.Core.Level.Info);
                            createBlocky();
                            break;
                    }
                    */
                    break;
                case BuildingType.BUILDING_CIVIL:
                    //createTower();
                    break;
                case BuildingType.BUILDING_MILITARY:
                    break;
                case BuildingType.BUILDING_HEALTHCARE:
                    break;
                case BuildingType.BUILDING_SPORTS:
                    break;
                case BuildingType.BUILDING_ENTERTAINMENT:
                    break;
                case BuildingType.BUILDING_EDUCATION:
                    break;
                case BuildingType.BUILDING_RELIGIOUS:
                    break;
                case BuildingType.BUILDING_MUSEUM:
                    break;
                case BuildingType.BUILDING_POWERSTATION:
                    break;
                case BuildingType.BUILDING_MINEOILGAS:
                    break;
                case BuildingType.BUILDING_ZOOLOGICAL:
                    break;
                case BuildingType.BUILDING_CEMETARY:
                    break;
                case BuildingType.BUILDING_PRISON:
                    break;
                case BuildingType.BUILDING_AGRICULTURAL:
                    break;
                case BuildingType.BUILDING_RECREATION:
                    break;
                default:
                    //createSimple();
                    break;
            }
        }

        #endregion
    }
}
