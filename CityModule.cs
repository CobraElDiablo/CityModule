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
using OpenSim.Region;
using OpenSim.Region.CoreModules;
using OpenSim.Region.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
//  This needs to be changed to Aurora.CoreApplicationPlugins, it was working
// but now has revereted to not recognising the namespace despite having added
// the dll as a reference and the project itself as a dependancy of City Builder.
//using Aurora.CoreApplicationPlugins;
using OpenSim.Framework;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

//  Add support for new namespace that deals with geospatial data types and processing
// of basic data forms along with the communications and storage of these data types.
using Aurora.Modules.CityBuilder.GeoSpatial.DataTYpes;

namespace Aurora.Modules.CityBuilder
{

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
    public class CityModule : IApplicationPlugin, ICityModule
    {
        /// <summary>
        /// This section of the module deals with the properties that are specific to the city or to the
        /// module itself. Some of the parameters are changeable via the set/get city commands on the console.
        /// </summary>
        #region Internal Members

        private UserAccount m_DefaultUserAccount = null;
        private string m_DefaultUserName = string.Empty;
        private string m_DefaultUserEmail = string.Empty;
        private string m_DefaultUserPword = string.Empty;

        private EstateSettings m_DefaultEstate = null;
        private string m_DefaultEstateName = string.Empty;
        private string m_DefaultEstateOwner = string.Empty;
        private string m_DefaultEstatePassword = string.Empty;

        private IUserAccountService m_UserAccountService = null;
        private IEstateConnector EstateConnector = null;
        private LandData cityLandData = new LandData();

        private static bool m_fGridMode = false;

        //  The start port number for any generated regions, this will increment for every
        // region that the plugin produces.
        private int startPort = 9500;
        //  Determines whether the plugin is enabled or not, if disabled then commands issued
        // on the command console will be ignored.
        private bool m_fEnabled = false;
        //  Has the plugin been initialised (installed).
        private bool m_fInitialised = false;
        //  The random value to use for city generation.
        private int citySeed = 0;
        // The name of the city, TODO add some for of random name generation for not only the
        // city name but also for each region that is created.
        private string cityName = string.Empty;
        // The owners name (avatar name first/last) that owns the entire region, defaults to
        // nothing (same as UUID.Zero) which means it's owned by the server and not an avatar.
        private string cityOwner = string.Empty;
        private string CityEstate = string.Empty;
        //  For logging purposes.
        public static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        //  A map of the city, includes plots, buildings and all regions.
        private CityMap cityMap = null;
        //  Configuration for the plugin.
        private IConfig cityConfig = null;
        //  Configuration source from Aurora.
        private IConfigSource configSource = null;
        //  Scene graph.
        public SceneGraph sceneGraph = null;
        //  Scene manager for region creation.
        public SceneManager sceneManager = null;
        // Simulation base from Aurora.
        private ISimulationBase simulationBase = null;
        // Densities for various parts of the city, residential, commercial, industrial etc.
        private List<float> cityDensities = new List<float>();
        private Vector2 m_DefaultStartLocation = new Vector2(9500, 9500);
        private Random rnd = new Random();

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

            cityDensities.Add(0.85f);
            cityDensities.Add(0.65f);
            cityDensities.Add(0.60f);
            cityDensities.Add(0.56f);

            cityMap = new CityMap();
            if (cityMap.cityRegions.GetUpperBound(0) <= 0 || cityMap.cityRegions.GetUpperBound(1) <= 0)
            {
                cityMap.cityRegions = new Scene[1,1];
            }
            m_fInitialised = true;
        }

        public int randomValue(int range)
        {
            int r = 0;
            r = rnd.Next(range);
            return r;
        }

        private void doBackup()
        {
        }

        private void doRestore()
        {
        }

        private void doList()
        {
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="GTAIV"></param>
        /// <returns></returns>
        private bool doExport(string filePath, bool GTAIV)
        {
            if (GTAIV) return (false);
            return (false);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="GTAIV"></param>
        /// <returns></returns>
        private bool doImport(string filePath, bool GTAIV)
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
            plot.xpos = randomValue(256) / 4;
            plot.ypos = randomValue(256) / 4;
            plot.width = (byte)randomValue(10);
            plot.depth = (byte)randomValue(10);
            plot.plotFlags = PlotClaimType.CLAIM_BUILDING | PlotClaimType.CLAIM_COMPLEX;

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
//                m_log.Info("[CITY BUILDER]: Scene manager obtained constructing region");
                sceneManager.CreateRegion(regionInfo, out scene);
//                if (scene != null)
//                {
//                    cityMap.cityRegions[x, y] = (Scene)scene;
//                }
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
            int r = this.randomValue(16);// (int)(27 / 2.45f + (((4 / 5) * 4) / 3));

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
                m_UserAccountService.CreateUser(m_DefaultUserName, Util.Md5Hash(m_DefaultUserPword), m_DefaultUserEmail);
                m_DefaultUserAccount = m_UserAccountService.GetUserAccount(UUID.Zero, m_DefaultUserName);
                cityOwner = m_DefaultUserName;
            }

            //  Construct the Estate/parcel data for this user.
            m_DefaultEstate = new EstateSettings();

            m_DefaultEstate.EstateOwner = m_DefaultUserAccount.PrincipalID;
            m_DefaultEstate.EstateName = CityEstate;
            m_DefaultEstate.EstatePass = Util.Md5Hash(Util.Md5Hash(m_DefaultEstatePassword));

            //  Obtain the scene manager.
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

            if (m_DefaultEstate != null)
            {
                m_DefaultEstate.EstateOwner = m_DefaultUserAccount.PrincipalID;
                m_DefaultEstate.EstateName = CityEstate;
                m_DefaultEstate.EstatePass = Util.Md5Hash(Util.Md5Hash(m_DefaultEstatePassword));
                m_DefaultEstate.EstateID = (uint)this.randomValue(1000);
                EstateConnector = Aurora.DataManager.DataManager.RequestPlugin<IEstateConnector>();
                if (EstateConnector != null)
                {
                    regionInfo.EstateSettings = EstateConnector.CreateEstate(m_DefaultEstate, regionInfo.RegionID);
                }
                else
                {
                    m_log.Info("[CITY BUILDER]: Estate connector missing.");
                    return (false);
                }
                cityLandData.OwnerID = m_DefaultUserAccount.PrincipalID;
                cityLandData.Name = CityEstate;
                cityLandData.GlobalID = UUID.Random();
                cityLandData.GroupID = UUID.Zero;
            }

            int regionPort = startPort;

            //  Construct the region.
            regionInfo.RegionSizeX = cityConfig.GetInt("DefaultRegionSize", 256);
            regionInfo.RegionSizeY = regionInfo.RegionSizeX;
            regionInfo.RegionType = "Mainland";
            regionInfo.ObjectCapacity = 100000;
            regionInfo.Startup = StartupType.Normal;
            regionInfo.ScopeID = UUID.Zero;

            //  Construct the regions for the city.
            for (rx = 0; rx < r; rx++)
            {
                for (ry = 0; ry < r; ry++)
                {
                    regionInfo.RegionID = UUID.Random();
                    IPAddress address = IPAddress.Parse("0.0.0.0");
                    regionInfo.ExternalHostName = Aurora.Framework.Utilities.GetExternalIp();
                    regionInfo.FindExternalAutomatically = true;
                    regionInfo.InternalEndPoint = new IPEndPoint(address, regionPort++);
                    cityLandData.RegionID = regionInfo.RegionID;
                    regionInfo.RegionName = "Region" + rx + ry;
                    regionInfo.RegionLocX = (int)(m_DefaultStartLocation.X + rx);
                    regionInfo.RegionLocY = (int)(m_DefaultStartLocation.Y + ry);
                    if (!createRegion(rx, ry, regionInfo))
                    {
                        m_log.InfoFormat("[CITY BUILDER]: Failed to construct region at {0},{1}", rx, ry);
                        return (false);
                    }
                }
            }

            //  Either generate the terrain or loading from an existing file, DEM for example.
            m_log.Info("[CITY BUILDER]: [TERRAIN]");

            //  For each region, just fill the terrain to be 21. This is just above the default
            // water level for Aurora.
            float[,] tHeight = new float[256, 256];
            for (rx = 0; rx < 256; rx++)
            {
                for (ry = 0; ry < 256; ry++)
                {
                    tHeight[rx, ry] = 21.0f;
                }
            }
            //  Construct the new terrain for each region and pass the height map to it.
            for (rx = 0; rx < r; rx++)
            {
                for (ry = 0; ry < r; ry++)
                {
                    Scene region = cityMap.cityRegions[rx, ry];
                    ITerrainChannel tChannel = new TerrainChannel(true, region);
                    ITerrain terrain = null;
                    try
                    {
                        region.TryRequestModuleInterface<ITerrain>(out terrain);
                        terrain.SetHeights2D(tHeight);
                    }
                    catch
                    {
                    }
                }
            }
            //  From the total number of regions pick a number of regions that will be 'centers'
            // for the entire city, record these in the centralRegions list.
            m_log.Info("[CITY BUILDER]: [CENTERS]");
            //  ( region count * region count ) / 3
            int aNum = this.randomValue((cityMap.cityRegions.GetUpperBound(0) * cityMap.cityRegions.GetUpperBound(1))/3);
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
            m_log.Info("[CITY BUILDER]: [FREEWAYS]");
            m_log.Info("[CITY BUILDER]: [HIGHWAYS]");
            m_log.Info("[CITY BUILDER]: [STREETS]");
            m_log.InfoFormat("[CITY BUILDER]: [RESIDENTIAL DENSITY] {0}%", cityDensities[0] * 100);
            m_log.InfoFormat("[CITY BUILDER]: [COMMERCIAL DENSITY] {0}%", cityDensities[1] * 100);
            m_log.InfoFormat("[CITY BUILDER]: [CORPORATE DENSITY] {0}%", cityDensities[2] * 100);
            m_log.InfoFormat("[CITY BUILDER]: [INDUSTRIAL DENISTY] {0}%", cityDensities[3] * 100);
            m_log.Info("[CITY BUILDER]: [BLOCKS]");
            m_log.Info("[CITY BUILDER]: [ALLOTMENT PLOTS]");
            m_log.Info("[CITY BUILDER]: [BUILDINGS]");

            return (true);
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
                citySeed = this.randomValue(257);
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
                citySeed = this.randomValue(257);
                cityName = string.Empty;
                cityOwner = string.Empty;
                CityEstate = string.Empty;
                //  Configuration for the plugin.
                //  Configuration source from Aurora.
                cityConfig = new ConfigBase("CityBuilder",configSource);
                cityDensities = new List<float>();
                cityDensities.Add(0.85f);
                cityDensities.Add(0.75f);
                cityDensities.Add(0.65f);
                cityDensities.Add(0.45f);
                m_DefaultStartLocation = new Vector2(9500, 9500);
                // automatically disable the module if a configuration is not found. You can
                // manually enable the module and then set its internal properties before using
                // it via the server command console prompt.
                m_fEnabled = false;
            }

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
//            if (cmdParams.Length != 6 || cmdParams.Length != 7)
//            {
//                m_log.InfoFormat("[CITY BUILDER] : Bad parameters supplied to city generate. {0}",cmdParams.Length);
//                m_log.Info("[CITY BUILDER] : city generate <region> <seed> <city name> <city owner> <all/single>");
//                return;
//            }

            doGenerate(this.randomValue(32767));
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
