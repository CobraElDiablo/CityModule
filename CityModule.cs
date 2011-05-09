/*
 *
 *  City Builder Module for Aurora/OpenSim
 *       
 *  $filename   ::  CityModule.cs
 *  $author     ::  Cobra El Diablo
 *  
 */

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Net;
using System.Xml;
using System.Xml.Serialization;

using log4net;

using Nini;
using Nini.Config;

using OpenMetaverse;
using OpenMetaverse.StructuredData;

using Aurora.Services.DataService;
using Aurora.Simulation.Base;
using Aurora.Framework;

using OpenSim;
using OpenSim.Data;
using OpenSim.Framework;
using OpenSim.Framework.Serialization;
using OpenSim.Region;
using OpenSim.Region.CoreModules;
using OpenSim.Region.CoreModules.World.Warp3DMap;
using OpenSim.Region.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
//  This needs to be changed to Aurora.CoreApplicationPlugins, it was working
// but now has revereted to not recognising the namespace despite having added
// the dll as a reference and the project itself as a dependancy of City Builder.
//using Aurora.CoreApplicationPlugins;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

//  Add support for new namespace that deals with geospatial data types and processing
// of basic data forms along with the communications and storage of these data types.
using Aurora.Modules.CityBuilder.GeoSpatial.DataTypes;

namespace Aurora.Modules.CityBuilder
{
    /// <summary>
    /// Defines the data set that is being currently used by City Builder. Valid datasets
    /// include a native format (currently in tar archives, but soon to be changed to use
    /// MySQL for the extra geospatial support). A GIS data format that allowd for direct
    /// use of standardised data for spaces, and a data format suitable for the import of
    /// game files from GTA IV.
    /// </summary>
    public enum DataSetType : int
    {
        DATASET_TYPE_NULL = -1,
        DATASET_TYPE_NATIVE,
        DATASET_TYPE_GEOSPATIAL,
        DATASET_TYPE_GTAIVPC,
        DATASET_TYPE_COUNT
    };

    /// <summary>
    /// The process of generating an enitre city scape from the terrain to the final finishes touches
    /// takes on many steps and are presented below.
    /// </summary>
    public enum GenerationStage : int
    {
        GENSTAGE_PRESTAGE = -1,
        GENSTAGE_INITIALISING,
        GENSTAGE_TERRAIN,
        GENSTAGE_CENTERS,
        GENSTAGE_DENSITY,
        GENSTAGE_FREEWAYS,
        GENSTAGE_HIGHWAYS,
        GENSTAGE_STREETS,
        GENSTAGE_RESIDENTIAL_DENSITY,
        GENSTAGE_COMMERCIAL_DENSITY,
        GENSTAGE_CORPORATE_DENSITY,
        GENSTAGE_INDUSTRIAL_DENISTY,
        GENSTAGE_BLOCKS,
        GENSTAGE_ALLOTMENT_PLOTS,
        GENSTAGE_BUILDINGS,
        GENSTAGE_COUNT
    };

    /// <summary>
    /// This is the main class that deals with this City Builder Module for Aurora/OpenSim server.
    /// </summary>
    [Serializable]
    public class CityModule : IDataTransferable, IApplicationPlugin, ICityModule
    {
        #region City Module Constants
        public string[] companyNamePrefixes = new string[] { 
            "i", "Green", "Mega", "Super", "Omni", "e", "Hyper", "Global", "Vital", "Next",
            "Pacific", "Metro", "Unity", "G-", "Trans", "Infinity", "Superior", "Monolith",
            "Best", "Atlantic", "First", "Union", "National", "Inter National"
        };
        public string[] companyNames = new string[] {
            "Biotic", "Info", "Data", "Solar", "Aerospace", "Motors", "Nano", "Online", "Circuits",
            "Energy", "Med", "Robotic", "Exports", "Security", "Systems", "Industrial", "Media",
            "Materials", "Foods", "Networks", "Shipping", "Tools", "Medical", "Publishing",
            "Enterprise", "Audio", "Health", "Bank", "Imports", "Apparel", "Petroleum", "Studios" };
        public string[] companyNameSuffixs = new string[] {
            "Corp", "Inc", ".com", "USA", "Ltd", "Net", "Tech", "Labs", "Mfg", "UK", "Unlimited", "One", "LLC" };
        #endregion
        /// <summary>
        /// This section of the module deals with the properties that are specific to the city or to the
        /// module itself. Some of the parameters are changeable via the set/get city commands on the console.
        /// </summary>
        #region Internal Members
        private GenerationStage m_CurrentStage = GenerationStage.GENSTAGE_PRESTAGE;
        [XmlIgnore]
        private UserAccount m_DefaultUserAccount = null;
        private string m_DefaultUserName = string.Empty;
        private string m_DefaultUserEmail = string.Empty;
        [XmlIgnore]
        private string m_DefaultUserPword = string.Empty;
        [XmlIgnore]
        private EstateSettings m_DefaultEstate = null;
        private string m_DefaultEstateName = string.Empty;
        private string m_DefaultEstateOwner = string.Empty;
        [XmlIgnore]
        private string m_DefaultEstatePassword = string.Empty;
        [XmlIgnore]
        private IUserAccountService m_UserAccountService = null;
        [XmlIgnore]
        private IEstateConnector EstateConnector = null;
        private LandData cityLandData = new LandData();
        private static bool m_fGridMode = false;
        //  The start port number for any generated regions, this will increment for every
        // region that the plugin produces.
        private int startPort = 9500;
        //  Determines whether the plugin is enabled or not, if disabled then commands issued
        // on the command console will be ignored.
        [XmlIgnore]
        private bool m_fEnabled = false;
        //  Has the plugin been initialised (installed).
        [XmlIgnore]
        private bool m_fInitialised = false;
        //  The random value to use for city generation.
        private int citySeed = 0;
        // The name of the city, TODO add some for of random name generation for not only the
        // city name but also for each region that is created.
        private string cityName = string.Empty;
        // The owners name (avatar name first/last) that owns the entire region, defaults to
        // nothing (same as UUID.Zero) which means it's owned by the server and not an avatar.
        private string cityOwner = string.Empty;
        [XmlIgnore] // Duplicate of m_DefaultEstateName, todo remove and change all references to m_DefaultEstateName.
        private string CityEstate = string.Empty;
        //  For logging purposes.
        [XmlIgnore]
        public static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        //  A map of the city, includes plots, buildings and all regions.
        private CityMap cityMap = null;
        //  Configuration for the plugin.
        [XmlIgnore]
        private IConfig cityConfig = null;
        //  Configuration source from Aurora.
        [XmlIgnore]
        private IConfigSource configSource = null;
        //  Scene manager for region creation.
        [XmlIgnore]
        public SceneManager sceneManager = null;
        [XmlIgnore]
        private SceneGraph sceneGraph = null;
        // Simulation base from Aurora.
        [XmlIgnore]
        private ISimulationBase simulationBase = null;
        // Densities for various parts of the city, residential, commercial, industrial etc.
        private List<float> cityDensities = new List<float>();
        private Vector2 m_DefaultStartLocation = new Vector2(9500, 9500);

        private List<System.Threading.Thread> m_ThreadPool = new List<System.Threading.Thread>();
        #endregion
        /// <summary>
        /// Internal methods private to the City Module.
        /// </summary>
        /// <param name="range"></param>
        /// <returns></returns>
        #region Internal Methods
        /// <summary>
        /// 
        /// </summary>
        /// <param name="range"></param>
        /// <returns></returns>
        private void InstallModule()
        {
            //
            //  CONSOLE COMMAND INTERFACE
            //
            //      Adds various commands to the server's console to allow for direct manipulation
            // of the module and it's internal properties.
            //
            //  Add a command city set to allow for the direct setting of the properties of the module.
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand(
                "city", true, "city set", "Sets an internal property to the given value.",
                "Allows for the manipulation of the internal property values in the City Builder Module",
                cmdSetParameter);

            //  Add a command city get to allow for the display of the current value for a module property.
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand(
                "city", true, "city generate", "Auto generate a city from given parameters.",
                "This command will generate a city across an entire region or server instance of regions " +
                "based on the given parameters", cmdGenerate);

            //export
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand("city", true, "city export",
                "Export the current settings for this city to a file.",
                "Exports current module settings for the city for each region that is part of the city",
                cmdExport);

            //import
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand("city", true, "city import",
                "Imports a previously saved city definition file, or import from GTA IV PC",
                "Imports the settings required for a given city and recreate it inworld on one or more regions",
                cmdImport);

            //help
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand("city", true, "city help",
                "Display help information",
                "Display some help information about the use of the City Builder module from the command console",
                cmdHelp);

            //reset
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand("city", true, "city reset",
                "Reset the internal properties of the module to the ones used during development.",
                "Reset properties to 'Cobra ElDiablo' (City Owner), 'Liberty City' (City Name), Lave (City Region [SINGLE])",
                cmdReset);

            // enable
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand("city", true, "city enable",
                "Enables the City Module.", "Allows for the module to be re-enabled after changing internal properties",
                cmdEnable);

            // disable
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand("city", true, "city disable",
                "Disables the City Module.", "Allows for the module to be disabled before altering internal properties.",
                cmdDisable);

            //  add a command 'city info' to the main console.
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand(
                "city", true, "city info", "Displays information from the City Builder Module.",
                "Displays information about the current parameters from the city, like the number of buildings.",
                cmdInfo);

            //  add a 'city building' command (note has subcommands)
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand(
                "city", true, "city building", "City building interface",
                "Allow for the manipulation of buildings directly.",
                cmdBuilding);

            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand(
                "city", true, "city backup", "Backup the city instance to disk/database.",
                "Allows for the generated city to be backed up to disk or database.",
                cmdBackup);

            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand(
                "city", true, "city restore", "Restores a city from disk or database.",
                "Allows for previously backed up cities to be restored from disk or database.",
                cmdRestore);

            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand(
                "city", true, "city list", "List all known cities",
                "Displays a list of all cities present on disk or database.",
                cmdList);

            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand(
                "city", true, "city builder", "", "Opens a GUI editor for city generation parameter tweaking.",
                cmdCityBuilder);

            m_fInitialised = true;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="range"></param>
        /// <returns></returns>
        public static int randomValue(int range)
        {
            Random rnd = new Random();
            int r = 0;
            r = rnd.Next(range);
            return r;
        }
        /// <summary>
        /// 
        /// </summary>
        private void doBackup()
        {
        }
        /// <summary>
        /// 
        /// </summary>
        private void doRestore()
        {
        }
        /// <summary>
        /// 
        /// </summary>
        private void doList()
        {
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="GTAIV"></param>
        /// <returns></returns>
        private bool doExport(string filePath, DataSetType data_type )
        {
            //  Initial stage for the export/import functionality is the ability to export/import
            // the data correctly for the current city, uses OSDMap. For now the GTAIV flag is 
            // ignored, this needs to be changed to allow for internal, GTA and GIS data sets to 
            // be used for cities.
            if (filePath == null || data_type == DataSetType.DATASET_TYPE_NULL)
            {
                return (false);
            }

            //  Make sure the file is not present on the destination.
            // ???

            // First stage use a TarArchiveWriter in conjunction with OSDMap to write a file
            // containing all the information needed about the city and regions to be able to
            // import it in when the server is shutdown and restarted.
            System.IO.MemoryStream data = new System.IO.MemoryStream(1024);
            TarArchiveWriter tarArchive = null;

            //  Construct the archive before writing it to the destination.
            tarArchive = new TarArchiveWriter(data);
            tarArchive.WriteDir("citybuilder/");
            tarArchive.WriteFile(filePath, data.GetBuffer());

            return (false);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="GTAIV"></param>
        /// <returns></returns>
        private bool doImport(string filePath, DataSetType data_type )
        {
            //  Import the file from the specified file path. For now the GTAIV flag is
            // ignored.

            m_log.InfoFormat("[CITY BUILDER]: Importing from {0}", filePath);
            return (false);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="x1"></param>
        /// <param name="z1"></param>
        /// <param name="direction"></param>
        private void doLightStrip(int x1, int z1, int direction)
        {
            ;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="x1"></param>
        /// <param name="?"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        private void doRoad(int x1, int y1, int width, int depth)
        {
            int lanes;
            int divider;
            int sidewalk;
            if (width > depth)
                lanes = depth;
            else
                lanes = width;
            if (lanes < 4)
                return;
            bool odd = false;
            int i = (lanes % 2);
            if (i > 0) odd = true;
            if (odd)//lanes % 2)
            {
                lanes--;
                divider = 1;
            }
            else
                divider = 0;
            sidewalk = 2;// MAX(2, (lanes - 10));
            lanes -= sidewalk;
            sidewalk /= 2;
            lanes /= 2;
            cityMap.ClaimPlot(cityMap.MakePlot(x1, y1, width, depth, PlotClaimType.CLAIM_NONE));
            if (width > depth)
            {
                cityMap.ClaimPlot(cityMap.MakePlot(x1, y1 + sidewalk, width, lanes, PlotClaimType.CLAIM_TRANSPORT));
                cityMap.ClaimPlot(cityMap.MakePlot(x1, y1 + sidewalk + lanes + divider, width, lanes, PlotClaimType.CLAIM_TRANSPORT));
            }
            else
            {
                cityMap.ClaimPlot(cityMap.MakePlot(x1 + sidewalk, y1, lanes, depth, PlotClaimType.CLAIM_TRANSPORT));
                cityMap.ClaimPlot(cityMap.MakePlot(x1 + sidewalk + lanes + divider, y1, lanes, depth, PlotClaimType.CLAIM_TRANSPORT));
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="plot"></param>
        private void doBuilding()
        {
            //  Construct a random building and place it into the buildings list.
            CityBuilding building;

            BuildingType type = BuildingType.BUILDING_CIVIL | BuildingType.BUILDING_GENERAL;
            BuildingFlags flags = BuildingFlags.BUILDING_FLAG_ACOND | BuildingFlags.BUILDING_FLAG_LIGHTS |
                BuildingFlags.BUILDING_FLAG_LOGO | BuildingFlags.BUILDING_FLAG_TRIM;
            BuildingPlot plot = new BuildingPlot();
            plot.XPos = randomValue(256) / 4;
            plot.YPos = randomValue(256) / 4;
            plot.Width = (byte)randomValue(10);
            plot.Depth = (byte)randomValue(10);
            plot.PlotClaimType = PlotClaimType.CLAIM_BUILDING | PlotClaimType.CLAIM_COMPLEX;

            building = new CityBuilding(type, plot, flags, UUID.Zero,cityMap.centralRegions[0],"Building");
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public bool createRegion(int x, int y, RegionInfo regionInfo)
        {
            /*
             *  Construct a region for the given position in the city.
             */

            // Validate the supplied parameters and internal ones.
            // If a region already exists at the specified position just exit.
            if (!m_fEnabled || !m_fInitialised || cityConfig==null)
            {
                m_log.Info("[CITY BUILDER]: FAIL! not enabled, initialised or no configuration");
                return (false);
            }

            if (cityMap.Equals(null) || cityMap.cityRegions.Equals(null))
                return (false);

            //  Now ask the scene manager to construct the region.
            if (!sceneManager.Equals(null))
            {
                IScene scene = (IScene)cityMap.cityRegions[x, y];
                sceneManager.CreateRegion(regionInfo, out scene);
            }
            else
            {
                m_log.Info("[CITY BUILDER]: NO SCENE MANAGER");
                return (false);
            }

            //  Job done, exit with OK.
            return (true);
        }
        /// <summary>
        /// This method will produce a random city with the central region of the city being
        /// specified as a parameter. More parameters need to be made available for this method
        /// to produce a better quality city, note for now the minimum area for a city is a
        /// 3x3 grid of regions. This code is based on the original C++ version called pixel city.
        /// </summary>
        /// <param name="seed_value">Random integer seed value.</param>
        /// <returns>true / false indicator of success or failure.</returns>
        private bool doGenerate(int seed_value)
        {
            int rx, ry;
            //  Based on the initial seed value populate the regions that this shared module 
            // is connected to, this means first get a list of the region, determine which
            // region is in the center of all the regions and set this as the hotzone, or
            // central part of the city (this is where the tallest/largest buildings will 
            // be created) and will extend out to cover virtually all of the connected
            // regions if desired. No support for aging of the buildings or the city exists
            // yet it is a possible course for the future of this module.

            //  First quick check to see if the module is enabled or not.
            if (!m_fEnabled)
            {
                m_log.Info("[CITY BUILDER]: Disabled, aborting auto generation.");
                return (false);
            }

            m_log.Info("[CITY BUILDER]: Auto generating the city.");

            //  Now we need to ask some basic values for the city generation, we already have
            // the base seed value as this is part of the 'city generate' command, now what
            // about a name, position, size, densities etc. Some of this can be generated
            // based on the seed value, but then, it would need to be confirmed by the user
            // or allow them to change it. TODO move all requested data into the configuration file.
            if (m_UserAccountService == null)
            {
                m_UserAccountService = simulationBase.ApplicationRegistry.RequestModuleInterface<IUserAccountService>();
            }

            //  Decide where the city is to be placed within the server instance.
            int r = CityModule.randomValue(10);

            string regionCount = MainConsole.Instance.CmdPrompt("Region Count ", r.ToString());
            r = Convert.ToInt32(regionCount);
            m_log.InfoFormat("[CITY BUILDER]: City area {0} x {1} regions ", r, r);

            cityName = MainConsole.Instance.CmdPrompt("City Name ", cityName);
            cityOwner = MainConsole.Instance.CmdPrompt("City Owner ", cityOwner);
            m_DefaultUserName = cityOwner;

            //  Make sure that the user and estate information specified in the configuration file
            // have been loaded and the information has either been found or has been created.
            m_DefaultUserAccount = m_UserAccountService.GetUserAccount(UUID.Zero, cityOwner);
            if (m_DefaultUserAccount == null)
            {
                m_log.InfoFormat("[CITY BUILDER]: Creating default account {0}", m_DefaultUserName);
                m_UserAccountService.CreateUser(m_DefaultUserName, Util.Md5Hash(m_DefaultUserPword), m_DefaultUserEmail);
                m_DefaultUserAccount = m_UserAccountService.GetUserAccount(UUID.Zero, m_DefaultUserName);
                cityOwner = m_DefaultUserName;
            }
            else
                m_log.InfoFormat("[CITY BUILDER]: Account found for {0}", m_DefaultUserName);

            // Obtain the scene manager that the server instance is using.
            sceneManager = simulationBase.ApplicationRegistry.RequestModuleInterface<SceneManager>();

            //  Construct the data instance for a city map to hold the total regions in the simulation.
            cityMap = new CityMap();
            citySeed = seed_value;
            cityMap.cityRegions = new Scene[r, r];
            cityMap.cityPlots = new List<BuildingPlot>();
            cityMap.cityBuildings = new List<CityBuilding>();

            //  Construct land and estate data and update to reflect the found user or the newly created one.
            cityLandData = new LandData();
            RegionInfo regionInfo = new RegionInfo();

            regionInfo.RegionID = UUID.Random();

            //  Determine if the default user account as specified in City Builder's configuration file
            // has any predefined estates, if so, just select the first one for now. Perhaps a search of
            // the estates to attempt to find a match to the details from the configuration file.
            EstateConnector = Aurora.DataManager.DataManager.RequestPlugin<IEstateConnector>();
            // Valid estate connection established.
            if (EstateConnector != null)
            {
                //  Valid estate connector, determine if the default user account has any estates.
                List<EstateSettings> estates = EstateConnector.GetEstates(m_DefaultUserAccount.PrincipalID);
                // No estates are found, so construct a new one based on the default estate settings
                // from the configuration file.
                if (estates == null)
                {
                    // No estates present so construct one.
                    m_DefaultEstate = new EstateSettings();

                    m_log.InfoFormat("[CITY BUILDER]: No estates found for user {0}, constructing default estate.", m_DefaultUserAccount.Name);

                    m_DefaultEstate.EstateOwner = m_DefaultUserAccount.PrincipalID;
                    m_DefaultEstate.EstateName = m_DefaultEstateName;
                    m_DefaultEstate.EstatePass = Util.Md5Hash(Util.Md5Hash(m_DefaultEstatePassword));
                    m_DefaultEstate.EstateID = (uint)CityModule.randomValue(1000);

                    regionInfo.EstateSettings = EstateConnector.CreateEstate(m_DefaultEstate, regionInfo.RegionID);
                }
                else
                {
                    //  Estates have been found, select the first estate in the list. No checking is done
                    // against the configuration file settings.
                    m_DefaultEstate = estates[0];
                    regionInfo.EstateSettings = m_DefaultEstate;
                    m_log.InfoFormat("[CITY BUILDER]: {0} estates found for user {1}, selecting {2}",
                        estates.Count, m_DefaultUserAccount.Name, m_DefaultEstate.EstateName);
                }
            }
            else
            {
                m_log.Info("[CITY BUILDER]: No connection with server.");
                return (false);
            }

            //  Fill in land data for the estate/owner.
            cityLandData.OwnerID = m_DefaultUserAccount.PrincipalID;
            cityLandData.Name = m_DefaultEstateName;
            cityLandData.GlobalID = UUID.Random();
            cityLandData.GroupID = UUID.Zero;

            int regionPort = startPort;

            //  Construct the region.
            regionInfo.RegionSizeX = cityConfig.GetInt("DefaultRegionSize", 256);
            regionInfo.RegionSizeY = regionInfo.RegionSizeX;
            regionInfo.RegionType = "Mainland";
            regionInfo.ObjectCapacity = 100000;
            regionInfo.Startup = StartupType.Normal;
            regionInfo.ScopeID = UUID.Zero;

            IParcelServiceConnector parcelService = simulationBase.ApplicationRegistry.RequestModuleInterface<IParcelServiceConnector>();

            if (r == 1)
            {
                m_log.Info("[CITY BUILDER]: Single region city.");
                IPAddress address = IPAddress.Parse("0.0.0.0");
                regionInfo.ExternalHostName = Aurora.Framework.Utilities.GetExternalIp();
                regionInfo.FindExternalAutomatically = true;
                regionInfo.InternalEndPoint = new IPEndPoint(address, regionPort++);
                cityLandData.RegionID = regionInfo.RegionID;
                regionInfo.RegionName = "Region00";
                regionInfo.RegionLocX = (int)m_DefaultStartLocation.X;
                regionInfo.RegionLocY = (int)m_DefaultStartLocation.Y;
                if (parcelService != null)
                    parcelService.StoreLandObject(cityLandData);
//                EstateConnector.LinkRegion(regionInfo.RegionID, (int)m_DefaultEstate.EstateID, m_DefaultEstate.EstatePass);
                if (!createRegion(0, 0, regionInfo))
                {
                    m_log.Info("[CITY BUILDER]: Failed to construct region.");
                    return (false);
                }
            }
            else if (r > 1)
            {
                m_log.Info("[CITY BUILDER]: Multi-region city.");
                m_log.Info("[CITY BUILDER]: Finding external IP, please wait ... ");
                regionInfo.ExternalHostName = Aurora.Framework.Utilities.GetExternalIp();
                if (regionInfo.ExternalHostName.Length <= 0)
                {
                    regionInfo.FindExternalAutomatically = false;
                }
                else
                {
                    m_log.InfoFormat("[CITY BUILDER]: External IP address is {0}", regionInfo.ExternalHostName);
                    regionInfo.FindExternalAutomatically = true;
                }
                //  Construct the regions for the city.
                regionPort = startPort;
                INeighborService neighbours = simulationBase.ApplicationRegistry.RequestModuleInterface<INeighborService>();
                if (neighbours == null)
                {
                    m_log.Info("[CITY BUILDER]: No neighbours.");
                }
                else
                {
                    m_log.Info("[CITY BUILDER]: Neighbours service found.");
                }
                IPAddress address = IPAddress.Parse("0.0.0.0");

                for (rx = 0; rx < r; rx++)
                {
                    for (ry = 0; ry < r; ry++)
                    {
                        regionInfo.InternalEndPoint = new IPEndPoint(address, regionPort++);
                        cityLandData.RegionID = regionInfo.RegionID;
                        regionInfo.RegionName = "Region" + rx + ry;
                        regionInfo.RegionLocX = (int)(m_DefaultStartLocation.X + rx);
                        regionInfo.RegionLocY = (int)(m_DefaultStartLocation.Y + ry);
                        m_log.InfoFormat("[CITY BUILDER]: '{0}' @ {1},{2}, http://{3}/", regionInfo.RegionName,
                            regionInfo.RegionLocX, regionInfo.RegionLocY, regionInfo.InternalEndPoint);
                        if (parcelService != null)
                            parcelService.StoreLandObject(cityLandData);
//                        EstateConnector.LinkRegion(regionInfo.RegionID, (int)m_DefaultEstate.EstateID, m_DefaultEstate.EstatePass);
                        if (!createRegion(rx, ry, regionInfo))
                        {
                            m_log.InfoFormat("[CITY BUILDER]: Failed to construct region at {0},{1}", rx, ry);
                            return (false);
                        }
                        if (neighbours != null)
                        {
                            m_log.Info("[CITY BUILDER]: Informing neighbours.");
                            neighbours.InformOurRegionsOfNewNeighbor(regionInfo);
                        }
                    }
                }
            }

            //  Either generate the terrain or loading from an existing file, DEM for example.
            m_log.Info("[CITY BUILDER]: [TERRAIN]");

            //  Construct the new terrain for each region and pass the height map to it.
            //  For the entire area covered by all of the regions construct a new terrain heightfield for it.
            // Also construct several maps that can be blended together in order to provide a suitablly natural
            // looking terrain which is not too flat or doesn't entirely consist of mountains.
            float[,] terrainMap;
            float[,] hMap1;
            float[,] hMap2;
            float[,] hMap3;
            float[,] hMap4;
            float[] bFactors = new float[4];
            int size = regionInfo.RegionSizeX;
//            r * cityConfig.GetInt("DefaultRegionSize");
            int y;
            int x;

            terrainMap = new float[ size * r, size * r ];
            hMap1 = new float[ size * r, size * r ];
            hMap2 = new float[ size * r, size * r ];
            hMap3 = new float[ size * r, size * r ];
            hMap4 = new float[ size * r, size * r ];

            for (x = 0; x < size; x++)
            {
                for (y = 0; y < size; y++)
                {
                    hMap1[x, y] = Perlin.noise2((float)x, (float)y);
                    hMap2[x, y] = Perlin.noise2((float)x, (float)y);
                    hMap3[x, y] = Perlin.noise2((float)x, (float)y);
                    hMap4[x, y] = Perlin.noise2((float)x, (float)y);
                }
            }

            m_log.Info("[CITY BUILDER]: Terrain built, blending.");

            //  Set blending factors.
            bFactors[0] = 0.75f;
            bFactors[1] = 0.55f;
            bFactors[2] = 0.35f;
            bFactors[3] = 0.05f;

            //  Blend the maps together.
            for (x = 0; x < size; x++)
            {
                for (y = 0; y < size; y++)
                {
                    terrainMap[x, y] = (hMap1[x, y] * bFactors[0]) + (hMap2[x, y] * bFactors[1]) +
                        (hMap3[x, y] * bFactors[2]) + (hMap4[x, y] * bFactors[3]);
                }
            }

            m_log.Info("[CITY BUILDER]: Blended, applying.");

            //  Set the height map of each region based on the newly created terrainMap.
            for (rx = 0; rx < r; rx++)
            {
                for (ry = 0; ry < r; ry++)
                {
                    Scene region = cityMap.cityRegions[rx, ry];
                    ITerrainChannel tChannel = new TerrainChannel(true, region);
                    ITerrain terrain = null;

                    m_log.InfoFormat("[CITY BUILDER]: Region [ {0}, {1} ]", rx, ry);

                    try
                    {
                        region.TryRequestModuleInterface<ITerrain>(out terrain);
                        float[,] tile = new float[ size, size ];
                        for (int i = 0; i < size; i++)
                        {
                            for (int j = 0; i < size; j++)
                            {
                                tile[i, j] = terrainMap[(rx * size) + i, (ry * size) + j];
                            }
                        }

                        if (terrain != null)
                            terrain.SetHeights2D(tile);
                    }
                    catch
                    {
                    }
                }
            }

            //  Rivers and other waterways. Randomly select a number of rivers for the entire area
            // and place them.
            int rCount = CityModule.randomValue(size / r);
            m_log.InfoFormat("[CITY BUILDER]: River count for entire area {0}", rCount);

            //  From the total number of regions pick a number of regions that will be 'centers'
            // for the entire city, record these in the centralRegions list.
            m_log.Info("[CITY BUILDER]: [CENTERS]");
            //  ( region count * region count ) / 3
            int aNum = CityModule.randomValue((cityMap.cityRegions.GetUpperBound(0) * cityMap.cityRegions.GetUpperBound(1))/3);
            if (aNum == 0)
            {
                aNum = 1;
            }
            m_log.InfoFormat("[CITY BUILDER]: Total regions {0}, selecting {1} regions for centers.", (r*r), aNum );
            int prevRegionX = 0;
            int prevRegionY = 0;
            while ( aNum > 0 )
            {
                int currRegionX = randomValue( cityMap.cityRegions.GetUpperBound(0) ) / 2;
                int currRegionY = randomValue( cityMap.cityRegions.GetUpperBound(1) ) / 2;

                // If the location selected is the same as the previous location try again.
                if (currRegionX == prevRegionX && currRegionY == prevRegionY)
                {
                    aNum--;
                    continue;
                }

                m_log.InfoFormat("[CITY BUILDER]: Region {0}, located {1},{2}", aNum, prevRegionX, prevRegionY);

                try
                {
                    Scene region = cityMap.centralRegions[(prevRegionX * cityMap.cityRegions.GetUpperBound(0)) + prevRegionY];
                    if (region!=null)
                    {
                        cityMap.centralRegions.Add(region);
                    }
                }
                catch
                {
                }
                aNum--;
                prevRegionX = currRegionX;
                prevRegionY = currRegionY;
            }

            m_log.Info("[CITY BUILDER]: [DENSITY]");
            float avgDensity = 0.0f;
            
            avgDensity += cityDensities[0];
            avgDensity += cityDensities[1];
            avgDensity += cityDensities[2];
            avgDensity += cityDensities[3];
            avgDensity /= 4;

            //  Before ANYTHING else is created construct the transport systems, priority is given
            // to the road network before the rail network, perhaps a configuration option to allow
            // for the prioritisation value of the transport system is possible.
            m_log.Info("[CITY BUILDER]: [FREEWAYS]");

            //  Construct a road system (high speed ~50-70 mph) between and around the city center regions.


            m_log.Info("[CITY BUILDER]: [HIGHWAYS]");

            m_log.Info("[CITY BUILDER]: [STREETS]");

            m_log.Info("[CITY BUILDER]: [RAILWAYS]");
            
            m_log.InfoFormat("[CITY BUILDER]: [RESIDENTIAL DENSITY] {0}%", cityDensities[0] * 100);
            m_log.InfoFormat("[CITY BUILDER]: [COMMERCIAL DENSITY] {0}%", cityDensities[1] * 100);
            m_log.InfoFormat("[CITY BUILDER]: [CORPORATE DENSITY] {0}%", cityDensities[2] * 100);
            m_log.InfoFormat("[CITY BUILDER]: [INDUSTRIAL DENISTY] {0}%", cityDensities[3] * 100);
            m_log.InfoFormat("[CITY BUILDER]: [AVERAGE DENSITY] {0}%", avgDensity);

            m_log.Info("[CITY BUILDER]: [BLOCKS]");
            m_log.Info("[CITY BUILDER]: [ALLOTMENT PLOTS]");
            m_log.Info("[CITY BUILDER]: [BUILDINGS]");

            return (true);
        }

        #endregion
        #region Public Properties
        public GenerationStage GenerationStage
        {
            get { return (m_CurrentStage); }
        }
        #endregion
        /// <summary>
        /// This section deals with the inherited interface IApplicationPlugin, note some methods
        /// are duplicates of ones in the ISharedRegionModule interface region.
        /// </summary>
        #region IApplicationPlugin Region
        /// <summary>
        /// This method is during the startup of the Aurora server, It is called just after the
        /// HTTP server has started and before any shared modules or regions have been loaded.
        /// </summary>
        /// <param name="config">Configuration parameter stream.</param>
        public void Initialize(ISimulationBase openSim)
        {
            //  Display a startup message, save the passed 'server instance?', obtain the
            // scene manager and scene graph for this server instance and obtain the interface
            // used to access region information, regardless of whether the storage method is
            // file, web or MySQL based in reality. Finally call an internal method that installs
            // the command console commands and alters some internal properties, indicating that
            // the module is loaded, enabled and ready for use.
            m_log.Info("[CITY BUILDER]: Version 0.0.0.10 ");

            //  Store the supplied simulation base to for future use.
            simulationBase = openSim;
            //  Store the configuration source (I presume all contents of all ini files).
            configSource = simulationBase.ConfigSource;
            //  Store the configuration section specifically used by City Builder.
            cityConfig = configSource.Configs["CityBuilder"];

            //  Obtain the default user account service, do the same for the estate/parcel too.
            m_UserAccountService = simulationBase.ApplicationRegistry.RequestModuleInterface<IUserAccountService>();

            //  Register the ICityModule interface with the simulation base.
            simulationBase.ApplicationRegistry.RegisterModuleInterface<ICityModule>(this);
            m_log.Info("[CITY BUILDER]: ICityModule interface registered with simulation base.");

            //  If we have a configuration source for City Builder then set the specified internal properties else default them.
            if (cityConfig != null)
            {
                //  Configuration file is present or it is included within one of the other configuration
                // file that control aurora obtain the specified values or use hardcoded defaults.
                m_log.Info("[CITY BUILDER]: Configuration found, stored.");

                //  Construct Land data to be used for the entire city and any occupied regions.
                m_DefaultUserAccount = null;
                // Construct the estate settings for the city.
                m_DefaultEstate = null;

                startPort = cityConfig.GetInt("DefaultStartPort", startPort);

                m_fEnabled = cityConfig.GetBoolean("Enabled", m_fEnabled);

                m_fInitialised = false;
                citySeed = CityModule.randomValue(257);
                cityName = cityConfig.GetString("DefaultCityName", "CityVille");
                cityOwner = cityConfig.GetString("DefaultCityOwner", "Cobra ElDiablo");
                m_DefaultUserName = cityOwner;
                m_DefaultUserEmail = cityConfig.GetString("DefaultUserEmail", "");
                m_DefaultUserPword = cityConfig.GetString("DefaultUserPassword", "");
                CityEstate = cityConfig.GetString("DefaultCityEstate", "Liquid Silicon Developments");
                m_DefaultEstateOwner = cityOwner;
                m_DefaultEstatePassword = cityConfig.GetString("DefaultEstatePassword", "");
                cityDensities = new List<float>();
                m_DefaultStartLocation = new Vector2(9500, 9500);
                startPort = cityConfig.GetInt("DefaultStartPort", 9500);
            }
            else
            {
                m_log.Info("[CITY BUILDER]: No configuration data found.");

                m_DefaultUserAccount = null;
                m_DefaultEstate = null;
                
                m_fEnabled = false;
                m_fInitialised = false;
                citySeed = CityModule.randomValue(257);
                cityName = string.Empty;
                cityOwner = string.Empty;
                CityEstate = string.Empty;
                //  Configuration for the plugin.
                //  Configuration source from Aurora.
                cityConfig = new ConfigBase("CityBuilder",configSource);
                cityDensities = new List<float>();
                m_DefaultStartLocation = new Vector2(9500, 9500);
                // automatically disable the module if a configuration is not found. You can
                // manually enable the module and then set its internal properties before using
                // it via the server command console prompt.
                m_fEnabled = false;
            }

            cityDensities.Add(0.85f);
            cityDensities.Add(0.75f);
            cityDensities.Add(0.65f);
            cityDensities.Add(0.45f);
            //  Install the module, does not alter the enabled flag! This allows for the plugin
            // to install some commands for the main servers console but prevents any use of 
            // the plugin until the internal properties are set correctly.
            InstallModule();
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="config"></param>
        public void ReloadConfiguration(IConfigSource config)
        {
            m_log.Info("CityModule.ReloadConfiguration");
        }
        /// <summary>
        /// 
        /// </summary>
        public void Start()
        {
            m_log.Info("CityModule.Start");
        }
        /// <summary>
        /// 
        /// </summary>
        public void PostStart()
        {
            m_log.Info("CityModule.PostStart");
        }
        /// <summary>
        /// 
        /// </summary>
        public void Close()
        {
            m_log.Info("[CITY BUILDER]: Terminating.");
            m_fEnabled = false;
        }
        /// <summary>
        /// This is called when the module has been loaded.
        /// </summary>
        public void PostInitialise()
        {
            m_log.Info("[CITY BUILDER] finished initialising.");
        }
        /// <summary>
        /// Returns the name of the module, "City Builder Module".
        /// </summary>
        public string Name
        {
            get { return "CityBuilder"; }
        }
        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            m_log.Info("CityModule.Dispose");
        }
        #endregion
        /// <summary>
        /// 
        /// </summary>
        #region ICityModule Methods

        public SceneManager SceneManager
        {
            get { return sceneManager; }
        }

        public SceneGraph SceneGraph
        {
            get { return sceneGraph; }
        }

        public Vector2 CitySize
        {
            get
            {
                Vector2 size = new Vector2((float)cityMap.cityRegions.GetUpperBound(0), (float)cityMap.cityRegions.GetUpperBound(1));
                return size;
            }
        }

        public IConfigSource ConfigSource
        {
            get
            {
                return configSource;
            }
        }

        #endregion
        /// <summary>
        /// 
        /// </summary>
        #region IDataTransferable Methods

//        public override CityModule Copy()
//        {
            //  construct a new class instance.
//            CityModule module = new CityModule();
            //  copy across the internal property settings.
            //  return the new copy for the class instance.
//            return (module);
//        }

        public override Dictionary<string, object> ToKeyValuePairs()
        {
            return (null);
        }

        public override OSDMap ToOSD()
        {
            return (null);
        }

        public override void FromOSD(OSDMap map)
        {
        }

        public override void FromKVP(Dictionary<string, object> KVP)
        {
            FromOSD(Util.DictionaryToOSD(KVP));
        }

        public override IDataTransferable Duplicate()
        {
            CityModule c = new CityModule();
            c.FromOSD(ToOSD());
            return (null);
        }

        #endregion
        /// <summary>
        /// Console command interface method.
        /// </summary>
        /// <param name="range"></param>
        /// <returns></returns>
        #region Command Region
        
        /// <summary>
        /// This method decodes the 'city set' console command.
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdSetParameter(string module, string[] cmdParams)
        {
            string param = string.Empty;
            string value = string.Empty;

            if (cmdParams.Length != 4)
            {
                m_log.InfoFormat("Invalid number of parameters supplied ({0})", cmdParams.Length);
                m_log.Info("city set <parameter> <value>");
                return;
            }

            param = cmdParams[2];
            value = cmdParams[3];

            m_log.InfoFormat("Attempting to set parameter {0} to {1}", param, value);

            /*
             *      Settable parameters for the City Builder plugin.
             *      
             *  Property Name       Type        Default
             *  
             *      Name            string      DEBUG: "Cobra ElDiablo", RELEASE: as per config file.
             *      Owner
             *      regioncount
             *      regionsize
             *      estate
             *      densities
             *      
             */
            if (param == "Name")
            {
                m_log.InfoFormat("City name changed from {0}, to {1}", cityName, value);
                cityName = value;
                return;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdGetParameter(string module, string[] cmdParams)
        {
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdExport(string module, string[] cmdParams)
        {
            m_log.InfoFormat("[CITY BUILDER] : Exporting to file {0}", cmdParams[2]);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdImport(string module, string[] cmdParams)
        {
            m_log.InfoFormat("[CITY BUILDER] : Importing from file {0}", cmdParams[2]);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdHelp(string module, string[] cmdParams)
        {
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdReset(string module, string[] cmdParams)
        {
            //  Reset the module, first disable, then reset then reenable.
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdEnable(string module, string[] cmdParams)
        {
            if (m_fEnabled)
                return;
            m_log.Info("[CITY BUILDER] : Enabling");
            m_fEnabled = true;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdDisable(string module, string[] cmdParams)
        {
            if (!m_fEnabled)
                return;
            m_log.Info("[CITY BUILDER] : Disabling.");
            m_fEnabled = false;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdGenerate(string module, string[] cmdParams)
        {
            doGenerate(CityModule.randomValue(32767));
        }
        /// <summary>
        /// Handles one of the builtin commands on the main server's command console, this command
        /// is 'city info' it displays various information about the city, regions and buildings
        /// on the main servers console.
        /// </summary>
        /// <param name="module">The name of the module, should be 'city' or the actual modules name.</param>
        /// <param name="cmdparams">A list of strings making up the command including the actual command at index 0.</param>
        public void cmdInfo(string module, string[] cmdParams)
        {
            if (cmdParams[1] == "info")
            {
                m_log.InfoFormat("[CITY BUILDER]: City Name    : {0}", cityName);
                m_log.InfoFormat("[CITY BUILDER]: City Owner   : {0}", cityOwner);
                m_log.InfoFormat("[CITY BUILDER]: Region count : {0}",
                    cityMap.cityRegions.GetUpperBound(0) * cityMap.cityRegions.GetUpperBound(1));
                m_log.Info("[CITY BUILDER]: Street information");
                m_log.InfoFormat("[CITY BUILDER]: Plot count   : {0}", cityMap.cityPlots.Count());
                m_log.InfoFormat("[CITY BUILDER]: Buildings    : {0}", cityMap.cityBuildings.Count());

                m_log.InfoFormat("[CITY BUILDER]: Default Account : {0}, {1}", m_DefaultUserName, m_DefaultUserEmail );

                m_log.InfoFormat("[CITY BUILDER]: Default Estate : {0} owned by {1}",
                    m_DefaultEstateName, m_DefaultEstateOwner );

                if (m_fGridMode)
                {
                    m_log.Info("[CITY BUILDER]: Grid mode.");
                }
                else
                {
                    m_log.Info("[CITY BUILDER]: Standalone mode.");
                }

                m_log.InfoFormat("[CITY BUILDER]: Start port {0}", startPort );
                m_log.InfoFormat("[CITY BUILDER]: Start location {0},{1}", m_DefaultStartLocation.X,
                    m_DefaultStartLocation.Y);

                if (cityConfig.Equals(null))
                {
                    m_log.Info("[CITY BUILDER]: No configuration found.");
                }
                else
                {
                    m_log.InfoFormat("[CITY BUILDER]: Configuration found {0}", cityConfig.Name);
                }

            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdBuilding(string module, string[] cmdParams)
        {
            m_log.Info("[CITY BUILDER] : Buildings commands.");
            string name = MainConsole.Instance.CmdPrompt("Building name ", "Building");
            string owner = MainConsole.Instance.CmdPrompt("Owner", cityOwner);
            doBuilding();

        }

        public void cmdBackup(string module, string[] cmdParams)
        {
        }

        public void cmdRestore(string module, string[] cmdParams)
        {
        }

        public void cmdList(string module, string[] cmdParams)
        {
            // provide a list of buildings, plots, regions, estates, owners etc etc based
            // on the parameters supplied at the command console prompt.
        }

        public void cmdCityBuilder(string module, string[] cmdParams)
        {
            m_log.Info("[CITY BUILDER]: Opening GUI editor.");
        }

        #endregion
        #region Public Methods
        /// <summary>
        /// Constructs a cube at a given location and size.
        /// </summary>
        /// <param name="pos" type="Vector3">The position of the cube, at it's center, [X,Y,Z]</param>
        /// <param name="dim" type="Vector3">The size of the cube.</param>
        /// <returns>A UUID for the newly created cube or UUID.Zero on failure.</returns>
        /// </summary>
        public PrimitiveBaseShape createCube(Vector3 pos, Vector3 dim, UUID TextureID)
        {
            //  Construct a cube at a given position, size and texture UUID.
            PrimitiveBaseShape cubeShape = PrimitiveBaseShape.CreateBox();// new PrimitiveBaseShape();
            cubeShape.ToOmvPrimitive(pos, Quaternion.Identity);
            cubeShape.Scale = dim;
            sceneGraph.AddNewPrim(UUID.Zero, UUID.Zero, pos, Quaternion.Identity, cubeShape);
            return cubeShape;
        }

        #endregion

    }

}
