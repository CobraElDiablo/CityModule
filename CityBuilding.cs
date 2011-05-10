/*
 *  $filename   ::  CityBuilding.cs
 *  $author     ::  Cobra El Diablo
 *  $purpose    ::  Definition of a building which given some basic parameters can
 *                  self construct itself using a set of simple shapes (dynamically)
 *                  or use a predefined set of assets which can be linked together
 *                  to form the outer shell of most city buildings.
 *
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.Reflection;

using log4net;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Region;
using OpenSim.Region.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

using Aurora.Framework;

namespace Aurora.Modules.CityBuilder
{
    /*
     *  Building usage this is a list of buildings and landmarks used by ArcGIS.
     *  
     * struct {
     *  major,          8 options needed (building,Military,locale,landmark,Recreation,racetrak,cemetry,complex)
     *  minor,          repeat for each sub class of building.
     *  subtype1,       for each actual building type. (perhaps this needs to be bigger than 8 bits?)
     *  subtype1 : byte; placeholder/filler for now.
     *  } buildingdef; // Idea.
     * 0    Building    General
     * 1    Building    Community Center
     * 2    Building    Museum
     * 3    Building    General                 Federal Center
     * 4    Building    General                 Masonic Home
     * 5    Building    General                 Orphanage
     * 6    Building    General                 Job Training Center
     * 7    Building    General                 Observatory
     * 8    Building    General                 Armoury
     * 9    Building    General                 Grange Hall
     * 10   Building    General                 Forest Headquarters
     * 11   Building    General                 Ranger District Office
     * 12   Building    General                 Other Forest Service Facility
     * 13   Building    General                 Mall
     * 14   Building    General                 Post Office
     * 15   Building    General                 Prison
     * 16   Building    General                 Radio/TV Station
     * 17   Building    General                 Locale
     * 20   Building    Religious               Place of Worship
     * 21   Building    Religious               Seminary
     * 22   Building    Religious               Convent or Monastary
     * 23   Building    Healthcare              Health Science Center
     * 24   Building    Healthcare              Pyschiatric Hospital
     * 25   Building    Healthcare              Nursing Home
     * 26   Building    Sports/Entertainment    Auditorium
     * 27   Building    Sports/Entertainment    Stadium
     * 28   Building    Sports/Entertainment    Arena
     * 29   Building    Sports/Entertainment    Amphitheater
     * 30   Building    Educational             School
     * 31   Building    Educational             University
     * 32   Building    Educational             Elementary/Nursery
     * 33   Building    Educational             Middle/Junior
     * 34   Building    Educational             High
     * 35   Building    Educational             College
     * 36   Building    Educational             Community College
     * 37   Building    Educational             Vocational/Technical
     * 38   Building    Civic                   First Station
     * 39   Building    Civic                   Library
     * 40   Building    Civic                   Police Station
     * 41   Building    Civic                   Court House
     * 42   Building    Civic                   City/Town Hall
     * 43   Building    Civic                   Capitol Building
     * 44   Building    Complex                 Educational
     * 45   Building    Complex                 Regligious
     * 46   Building    Complex                 Healthcare
     * 47   Building    Complex                 Recreation
     * 48   Building    Complex                 Agricultural
     * 49   Building    Complex                 Orphanage
     * 50   Building    Complex                 Prison
     * 51   Building    Complex                 Industrial
     * 52   Military    Fort                    Army Active Duty
     * 53   Military    Fort                    Army Reserve
     * 54   Military    Fort                    Army Closed
     * 55   Military    Base                    Airforce Active Duty
     * 56   Military    Base                    Airforce Reserve
     * 57   Military    Base                    Airforce Closed
     * 58   Military    Base                    Marine Active Duty
     * 59   Military    Base                    Marine Reserve
     * 60   Military    Base                    Marine Closed
     * 61   Military    Station                 Navy Active Duty
     * 62   Military    Station                 Navy Reserve
     * 63   Military    Station                 Navy Closed
     * 64   Military    Station                 Coast Guard Active Duty
     * 65   Military    Station                 Coast Guard Reserve
     * 66   Military    Station                 Coast Guard Closed
     * 67   Military    Induction Center        Active Duty
     * 68   Military    Induction Center        Reserver
     * 69   Military    Induction Center        Closed
     * 70   Military    Multi Branch            Active Duty
     * 71   Military    Multi Branch            Reserve
     * 72   Military    Multi Branch            Closed
     * 73   Locale      Built up area
     * 74   Locale      Mobile Home Park
     * 75   Locale      Archeological Site
     * 76   Landmark    Landfill
     * 77   Landmark    Zoo
     * 78   Landmark    Shopping Center
     * 79   Landmark    Reclaimed Strip Mine
     * 80   Landmark    Arboretum
     * 81   Landmark    Wild Animal Park
     * 82   Landmark    Zoological Park
     * 83   Landmark    Drive-in Theater
     * 84   Landmark    Quarry
     * 85   Landmark    Levee/Dike
     * 86   Landmark    Tank
     * 87   Landmark    Pit     Unconsolidated Material
     * 88   Landmark    Picnic Area
     * 89   Landmark    Strip Mine
     * 90   Landmark    Gas/Oil Well Field
     * 91   Landmark    Power Station           Nuclear
     * 92   Landmark    Power Station           Coal
     * 93   Landmark    Power Station           Coal/Other
     * 94   Landmark    Power Station           Solar
     * 95   Landmark    Power Substation
     * 96   Landmark    Power Station           Tidal
     * 97   Landmark    Power Station           Geothermal
     * 98   Landmark    Power Station           Hydro Electric
     * 99   Recreation  Golf Course
     * 100  Recreation  Golf Course & Country CLub
     * 101  Recreation  Amusement Park
     * 102  Recreation  Campsite
     * 103  Recreation  Polo Field
     * 104  Recreation  Country Club
     * 105  Recreation  Shooting Range
     * 106  Recreation  Gun Club
     * 107  Recreation  Swimming Pool
     * 108  Recreation  Raquet Club
     * 109  Recreation  Athletic Field
     * 110  Recreation  Track
     * 111  Recreation  Court
     * 112  Racetrack   Horse/Dog
     * 113  Racetrack   Automobile
     * 114  Racetrack   Drag Strip
     * 115  Building    Healthcare          Hospital
     * 116  Cemetery
     * 117  Complex     Education           University
     * 118  Complex     Education           College
     * 119  Complex     Education           Community College
     * 120  Complex     Education           Vocational/Technical
     * 121  Complex     Education           High School
     * 122  Complex     Education           Middle/Junior
     * 123  Complex     Education           Elementary/Nursery
     * 
     * ???  Complex     Station     Railway station
     *      Complex     Docks       Cargo
     *      Complex     Docks       Build
     *      Complex     Docks       Terminal
     *      Complex     Docks       Launch
     *      Complex     Airport
     *      Landmark    damm/lakes/beaches/mountains etc
     */

    /// <summary>
    /// If a plot of land that has been claimed by something in the city.
    /// </summary>
    public enum PlotClaimType : uint
    {
        CLAIM_NONE = 0,         //  Land is not claimed.
        CLAIM_TRANSPORT=1,      //  The transport systems.
        CLAIM_PAVEMENT=2,       //  A pavement, at the edges of roads next to buildings.
        CLAIM_WATER=4,          //  A lake, river or other water feature.
        CLAIM_PARK=8,           //  A park or other open area with very little or no buildings.
        CLAIM_BUILDING=16,      //  A single building.
        CLAIM_COMPLEX=32,       //  Claimed as part of a larger set of buildings. This is the most common claim type.
        // And note the complex can have lots of different buildings which may just have the connection of being
        // on the same plot and have no 'business' with each other.
        CLAIM_MILITARY=64,      //  Claimed by the military for nafarious purposes.
        CLAIM_LANDMARK=128,     //  This claimed is landmarked.
        CLAIM_LOCALE=256,       //  Unique to this claim, unknown tag for now. Local style buildings unique to area.
        CLAIM_COUNT             //  END_OF_ENUM_MARKER
    };

    /// <summary>
    /// 
    /// </summary>
    public enum TransportType : byte
    {
        TRANSPORT_FOOT = 0,
        TRANSPORT_ROAD = 1,
        TRANSPORT_RAIL = 2,
        TRANSPORT_COUNT
    }

    /// <summary>
    /// This is mainly describing a rough type of the building, is it made from cubes or 
    /// does it have curved surfaces, think of this as the architectural type of the building.
    /// </summary>
    public enum BuildingType : int
    {
        BUILDING_NONE = -1,
        BUILDING_GENERAL = 0,
        BUILDING_LOCALE = 1,
        BUILDING_CIVIL = 2,
        BUILDING_EDUCATION = 4,
        BUILDING_HEALTHCARE = 8,
        BUILDING_SPORTS = 16,
        BUILDING_ENTERTAINMENT = 32,
        BUILDING_MILITARY = 64,
        BUILDING_RELIGIOUS = 128,
        BUILDING_MUSEUM = 256,
        BUILDING_POWERSTATION = 512,
        BUILDING_MINEOILGAS = 1024,
        BUILDING_ZOOLOGICAL = 2048,
        BUILDING_CEMETARY = 4096,
        BUILDING_PRISON = 8192,
        BUILDING_AGRICULTURAL = 16384,
        BUILDING_RECREATION = 32768,
        BUILDING_COUNT
    };

    public enum MilitaryBranch : byte
    {
        MILITARY_SERVICE_ARMY = 1,
        MILITARY_SERVICE_AIRFORCE=2,
        MILITARY_SERVICE_NAVY=4,
        MILITARY_SERVICE_MARINES=8,
        MILITARY_SERVICE_COASTGUARD=16,
        MILITARY_SERVICE_MULTIBRANCH=32,
        MILITARY_SERVICE_COUNT
    };

    public enum MilitaryBuildingType : byte
    {
        MILITARY_TYPE_FORT = 1,
        MILITARY_TYPE_BASE,
        MILITARY_TYPE_STATION,
        MILITARY_TYPE_INDUCTION,
        MILITARY_TYPE_MULTIBRANCH,
        MILITARY_TYPE_COUNT
    };

    /// <summary>
    ///  Flags that control the extra features that can be added to a building, for example
    /// lights on the corners of the building, if the building is above a certain height.
    /// Does the building have a corporate logo on one or more of it's faces.
    /// Does it have a lighting trim around the top of the building.
    /// Does it have a tower structure, usually water tower.
    /// Does it have a radio tower structure.
    /// Does it have an air conditioning unit(s) on the roof
    /// Counter for end of enum.
    /// 
    /// </summary>
    public enum BuildingFlags : byte
    {
        BUILDING_FLAG_NONE = 0,
        BUILDING_FLAG_LIGHTS = 1,   //  Navigation lights on the top corners of the building or on a pole.
        BUILDING_FLAG_TRIM = 2,     //  The trim around the top of the building has lighting.
        BUILDING_FLAG_LOGO = 4,     //  One or more faces of the building has a corporate logo.
        BUILDING_FLAG_TOWER = 8,    //  Building has a radio tower on the top.
        BUILDING_FLAG_ACOND = 16,   //  Building has air conditioning units.
        BUILDING_FLAG_HPAD = 32     //  Building has a helipad on the roof.
    };
    /// <summary>
    /// Grid based road system. No roading routing is implemented yet, and this style of
    /// road network maybe useful for inside a city center but beyond that the transport
    /// systems would need to take into account more terrain features that a city would.
    /// </summary>
    public enum RoadDirection : byte
    {
        MAP_ROAD_NORTH,
        MAP_ROAD_SOUTH,
        MAP_ROAD_EAST,
        MAP_ROAD_WEST
    };

    /// <summary>
    /// A plot of land upon which a building stands.
    /// </summary>
    [Serializable]
    public class BuildingPlot : IDataTransferable
    {
        #region Private Properties
        private int xpos;
        private int ypos;
        private int width;
        private int depth;
        private PlotClaimType plotFlags; // PlotClaimType.XXXXX
        #endregion
        #region Public Properties
        public int XPos
        {
            get { return (xpos); }
            set { xpos = value; }
        }
        public int YPos
        {
            get { return (ypos); }
            set { ypos = value; }
        }
        public int Width
        {
            get { return (width); }
            set { width = value; }
        }
        public int Depth
        {
            get { return (depth); }
            set { depth = value; }
        }
        public PlotClaimType PlotClaimType
        {
            get { return (plotFlags); }
            set { plotFlags = value; }
        }
        #endregion
        #region IDataTransferable Interface
        public override IDataTransferable Duplicate()
        {
            BuildingPlot plot = new BuildingPlot();
            plot.Duplicate();
            return (IDataTransferable)plot;
        }
        public override void FromKVP(Dictionary<string, object> KVP)
        {
            base.FromKVP(KVP);
        }
        public override Dictionary<string, object> ToKeyValuePairs()
        {
            return base.ToKeyValuePairs();
        }
        public override void FromOSD(OSDMap map)
        {
            XPos = map["XPos"].AsInteger();
            YPos = map["YPos"].AsInteger();
            Width = map["Width"].AsInteger();
            Depth = map["Depth"].AsInteger();
            PlotClaimType = (PlotClaimType)map["ClaimType"].AsUInteger();
            base.FromOSD(map);
        }
        public override OSDMap ToOSD()
        {
            OSDMap map = new OSDMap();

            map.Add("XPos", XPos);
            map.Add("YPos", YPos);
            map.Add("Width", Width);
            map.Add("Depth", Depth);
            map.Add("ClaimType", (byte)PlotClaimType);
            return map;
        }
        #endregion

    };

    /// <summary>
    ///  This class describes an individual building which is located on a plot of land
    /// in the city, the plot has a 2 meter border around the buildings footprint, it
    /// is also tries to make the plot a rectangular/square plot as the city is in a
    /// grid pattern spread over either a single region or multiple regions. Note that
    /// buildings will NOT overlap a regions boundary, if this case presents itself then
    /// a road/walkway should be used to provide a way of splitting city blocks over
    /// region boundaries. For example it would be better for the region boundary to be
    /// in the middle of a road rather than straight through a building as travelling
    /// from one side of the building to the other would induce a performance hit on the
    /// server, not too mention the fact that it could be somewhat irritating.
    /// </summary>
    [Serializable]
    public class CityBuilding : SceneObjectGroup
    {
        #region Internal Members
        //  Data properties for this instance of a building.
        private string          buildingName = string.Empty;
        private BuildingType    buildingType = BuildingType.BUILDING_GENERAL;
        private BuildingFlags   buildingFlags = BuildingFlags.BUILDING_FLAG_NONE;
        private Vector3         buildingCenter = Vector3.Zero;
        private BuildingPlot    buildingPlot = new BuildingPlot();
        private int buildingHeight = CityModule.randomValue(10);  // building height.
        //  This is a list of the texture identities used by the entire building, format of the
        // list is not decided yet, ie front,back,left,right or left,right,front,back (repeat for all) :P
        private List<UUID>      buildingTextures = new List<UUID>();
        //  This is a list of ALL the prims that make up this building as a whole, the root prim
        // of the building (which is the plot) is ALWAYS the first ie index 0 = building.plot
        // REMOVED, CHANGED TO BEING INHERITED FROM SceneObjectGroup A LIST OF ALL PARTS THAT
        // MAKE THIS GROUP (BUILDING) ARE ALREADY PRESENT WITHIN THIS CLASS OBJECT.
//        private List<UUID>      buildingPrims = new List<UUID>();
        //  Random seed allowing for the building to be auto generated programmatically based on
        // random(ish) values that during evaluation can be used to recreate an entire city scape
        // over multiple regions giving the same city every time.
        private int buildingSeed = CityModule.randomValue(65355);
        private int             buildingRoofTiers = 1;
        private Vector4         buildingColour = Vector4.Zero;
        private Vector4         buidlingTrimColour = Vector4.Zero;
        private UUID buildingGUID = UUID.Zero;  //  Unique to either a group (complex) or single building.
        private UUID buildingUUID = UUID.Zero;  //  Unique for this building
        private UUID buildingOwner = UUID.Zero; // Should this be the same as the SceneObjectGroup 'owner'?
        [XmlIgnore]
        private Scene scene = null; // Which scene or region this building belongs too, needed to primitive manipulation.
        #endregion

        #region Internal Methods

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private bool createSimple()
        {
            return false;
        }
        /// <summary>
        /// Constructs a blocky building this can be upto 10 floors in height (each floor is 3m).
        /// </summary>
        /// <returns></returns>
        private bool createBlocky()
        {
            return false;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private bool createModern()
        {
            return false;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private bool createTower()
        {
            return false;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="start_x"></param>
        /// <param name="start_y"></param>
        /// <param name="start_z"></param>
        /// <param name="direction"></param>
        /// <param name="length"></param>
        /// <param name="height"></param>
        /// <param name="window_groups"></param>
        /// <param name="uv_start"></param>
        /// <param name="blank_corners"></param>
        /// <returns></returns>
        private float constructWall(int start_x, int start_y, int start_z, int direction, int length, int height, int window_groups, float uv_start, bool blank_corners)
        {
            return 0.0f;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <param name="front"></param>
        /// <param name="back"></param>
        /// <param name="bottom"></param>
        /// <param name="top"></param>
        private void constructSpike(int left, int right, int front, int back, int bottom, int top)
        {
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <param name="front"></param>
        /// <param name="back"></param>
        /// <param name="bottom"></param>
        /// <param name="top"></param>
        private void constructCube(int left, int right, int front, int back, int bottom, int top)
        {
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <param name="front"></param>
        /// <param name="back"></param>
        /// <param name="bottom"></param>
        /// <param name="top"></param>
        private void constructCube(float left, float right, float front, float back, float bottom, float top)
        {
        }
        /// <summary>
        /// Constructs a cube at a given location and size.
        /// </summary>
        /// <param name="pos" type="Vector3">The position of the cube, at it's center, [X,Y,Z]</param>
        /// <param name="dim" type="Vector3">The size of the cube.</param>
        /// <returns>A UUID for the newly created cube or UUID.Zero on failure.</returns>
        /// </summary>
        private PrimitiveBaseShape createCube(Vector3 pos, Vector3 dim, UUID TextureID )
        {
            //  Construct a cube at a given position, size and texture UUID.
            PrimitiveBaseShape cubeShape = PrimitiveBaseShape.CreateBox();// new PrimitiveBaseShape();
            cubeShape.ToOmvPrimitive(pos, Quaternion.Identity);
            cubeShape.Scale = dim;

            // place into the region at given position
            // texture the object from the given texture id, if this texture is not found
            // then default to use one of the standard textures it knows about that
            // during first run it will create and store in the asset database (todo).
            return cubeShape;
//            return createCube(pos, dim, TextureID);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <param name="front"></param>
        /// <param name="back"></param>
        /// <param name="bottom"></param>
        private void constructRoof(float left, float right, float front, float back, float bottom)
        {
        }

        #endregion

        #region IDataTransferable Methods
        #endregion

        #region Public Methods

        public string BuildingName
        {
            get { return buildingName; }
            set { buildingName = value; }
        }

        #endregion

        #region Constructors

        public CityBuilding():base(null)
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
        public CityBuilding( BuildingType type, BuildingPlot plot, BuildingFlags flags, 
            UUID owner, IScene scene, string name ):base(owner,new Vector3(plot.XPos,21,plot.YPos),
            Quaternion.Identity, PrimitiveBaseShape.CreateBox(), name, scene)
        {
            //  Start the process of constructing a building given the parameters specified. For
            // truly random buildings change the following value (6) too another number, this is
            // used to allow for the buildings to be fairly fixed during research and development.
            buildingSeed = 6; // TODO FIX ACCESS TO THE CityModule.randomValue(n) code.
            buildingType = type;
            buildingPlot = plot;
            buildingFlags = flags;
            //  Has a valid owner been specified, if not use the default library owner (i think) of the zero uuid.
            if (!owner.Equals(UUID.Zero))
                buildingOwner = owner;
            else
                buildingOwner = UUID.Zero;

            //  Generate a unique value for this building and it's own group if it's part of a complex,
            // otherwise use the zero uuid for group (perhaps it should inherit from the city?)
            buildingUUID = UUID.Random();
            buildingGUID = UUID.Random();

            buildingCenter = new Vector3((plot.XPos + plot.Width / 2), 21, (plot.YPos + plot.Depth) / 2);
            if (name.Length > 0)
                buildingName = name;
            else
                buildingName = "Building" + type.ToString();
            //  Now that internal variables that are used by other methods have been set construct
            // the building based on the type, plot, flags and seed given in the parameters.
            switch (type)
            {
                case BuildingType.BUILDING_GENERAL:
                    OpenSim.Framework.MainConsole.Instance.Output("Building Type GENERAL", log4net.Core.Level.Info);
                    createBlocky();
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
                    createTower();
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
                    createSimple();
                    break;
            }
        }

        #endregion

    }

}
