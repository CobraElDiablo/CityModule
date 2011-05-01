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
        private EstateSettings m_DefaultEstate = null;
        private IUserAccountService m_UserAccountService = null;
        private LandData cityLandData = new LandData();

        //  The start port number for any generated regions, this will increment for every
        // region that the plugin produces.
        private int startPort = 9500;
        //  Determines whether the plugin is enabled or not, if disabled then commands issued
        // on the command console will be ignored.
        private bool m_fEnabled = false;
        //  Has the plugin been initialised (installed).
        private bool m_fInitialised = false;
        //  The random value to use for city generation.
        private int citySeed = CityModule.randomValue(257);
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
        //  Scene graph DEPRECIATED.
        public SceneGraph sceneGraph = null;
        //  Scene manager for region creation.
        public SceneManager sceneManager = null;
        // Simulation base from Aurora.
        private ISimulationBase simulationBase = null;
        // Region info connector (database) DEPRECIATED
        private IRegionInfoConnector m_connector = null;
        // Densities for various parts of the city, residential, commercial, industrial etc.
        private List<float> cityDensities = new List<float>();
        private Vector2 m_DefaultStartLocation = new Vector2(9500, 9500);

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

        public static int randomValue(int range)
        {
            int r = 0;
            Random rnd = new Random();
            r = rnd.Next(range);
            return r;
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
        public bool createRegion(int x, int y)
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

            //  Construct the user info for the region/estate.
            //  Decode the specified default user to be used for the owner of the estate and thus the city.
            string defaultUserAccountName = cityConfig.GetString("DefaultCityOwner", "Cobra ElDiablo");

            //  Find the users account or construct one.
            /*
            if (!m_UserAccountService.Equals(null))
                m_DefaultUserAccount = m_UserAccountService.GetUserAccount(UUID.Zero, defaultUserAccountName);
            else
                m_DefaultUserAccount = null;

            if (m_DefaultUserAccount.Equals(null))
            {
                //  Account doesn't exist, create it.
                m_log.InfoFormat("[CITY BUILDER]: IGNORE Constructing hardcoded account for user {0}", defaultUserAccountName);
//                m_DefaultUserAccount = m_UserAccountService.CreateUser(defaultUserAccountName, Util.Md5Hash("Chr1$Br00klyn"), "cobra@arcturus.bounceme.net");
            }
            else
                m_log.Info("[CITY BUILDER]: Specified user account found.");
            */

            //  Construct the estate/parcel info for this region.
            string defaultEstateName = cityConfig.GetString("DefaultCityEstate", "Liquid Silicon Developments");

            //  Construct land and estate data and update to reflect the found user or the newly created one.
            m_DefaultEstate = new EstateSettings();
            cityLandData = new LandData();

            cityLandData.OwnerID = UUID.Zero;// m_DefaultUserAccount.PrincipalID;
            cityLandData.Name = "Liquid Silicon Developments";
            cityLandData.GlobalID = UUID.Random();
            cityLandData.GroupID = UUID.Zero;

            m_DefaultEstate.EstateOwner = UUID.Zero;// m_DefaultUserAccount.PrincipalID;
            m_DefaultEstate.EstateName = "Liquid Silicon Developments";

            //  Construct the region.
            RegionInfo regionInfo = new RegionInfo();
            IScene scenePtr = cityMap.cityRegions[x, y];

//            regionInfo.CreateIConfig(configSource);
            regionInfo.EstateSettings = m_DefaultEstate;
            regionInfo.RegionID = UUID.Random();
            regionInfo.RegionSizeX = cityConfig.GetInt("DefaultRegionSize", 256);
            regionInfo.RegionSizeY = regionInfo.RegionSizeX;
            regionInfo.RegionType = "Mainland";
            regionInfo.ObjectCapacity = 100000;
            regionInfo.Startup = StartupType.Normal;
            regionInfo.ScopeID = m_DefaultEstate.EstateOwner;
            regionInfo.RegionName = "Region" + x + y;
            cityLandData.RegionID = regionInfo.RegionID;
            IPAddress address = IPAddress.Parse("0.0.0.0");
            regionInfo.InternalEndPoint = new IPEndPoint(address, startPort++);
            regionInfo.ExternalHostName = Aurora.Framework.Utilities.GetExternalIp();
            regionInfo.FindExternalAutomatically = true;

            //  Now ask the scene manager to construct the region.
            if (!sceneManager.Equals(null))
            {
                IScene scene = (IScene)cityMap.cityRegions[x, y];
                m_log.Info("[CITY BUILDER]: Scene manager obtained constructing region");
                sceneManager.CreateRegion(regionInfo, out scene);
            }
            else
            {
                m_log.Info("[CITY BUILDER]: NO SCENE MANAGER");
                return (false);
            }

            /*


            region.RegionType = "mainland";
            region.ObjectCapacity = 80000;//int.Parse(ObjectCount.Text);

            region.RegionSettings.Maturity = 0;
            region.Disabled = false;//DisabledEdit.Checked;
            region.RegionSizeX = 256;//int.Parse(CRegionSizeX.Text);
            region.RegionSizeY = 256;//int.Parse(CRegionSizeY.Text);
            if ((region.RegionSizeX % Constants.MinRegionSize) != 0 ||
                (region.RegionSizeY % Constants.MinRegionSize) != 0)
            {
                return(false);
            }
            region.RegionLocX = 100 + x * region.RegionSizeX;// * Constants.RegionSize);
            region.RegionLocY = 100 + y * region.RegionSizeY;// * Constants.RegionSize);
            region.NumberStartup = 0 + (x * y) + y;
            region.Startup = StartupType.Normal;

            m_log.Info("[CITY BUILDER]: Creating Region: (" + region.RegionName + ")");

            // set the initial ports
            region.HttpPort = MainServer.Instance.Port;

            AgentCircuitManager circuitManager = new AgentCircuitManager();
            IPAddress listenIP = region.InternalEndPoint.Address;
            Scene scene = new Scene();

            if (!IPAddress.TryParse(region.InternalEndPoint.Address.ToString(), out listenIP))
                listenIP = IPAddress.Parse("0.0.0.0");

            uint port = (uint)region.InternalEndPoint.Port;

            string ClientstackDll = configSource.Configs["Startup"].GetString("ClientStackPlugin", "OpenSim.Region.ClientStack.LindenUDP.dll");

            if (ClientstackDll.Length <= 0)
            {
                m_log.Info("[CITY BUILDER]: Unable to find ClientStackPlugin from configs");
                return (false);
            }

            IClientNetworkServer clientServer = AuroraModuleLoader.LoadPlugin<IClientNetworkServer>(ClientstackDll);
            IScene iScene;
            sceneManager.CreateRegion(region, out iScene);
            cityMap.cityRegions[x, y] = (Scene)iScene;
            bool fine = false;
            bool valid = false;
            int tries = 10;

            while (!fine)
            {
                if (tries <= 0)
                    break;
                try
                {
                    clientServer.Initialise(listenIP, ref port, 0, region.m_allow_alternate_ports,
                        configSource, circuitManager);
                    clientServer.AddScene(cityMap.cityRegions[x, y]);
                    m_log.InfoFormat("[CITY BUILDER]: Region {0} created @ {1},{2}", region.RegionName, x, y);
                    fine = true;
                    valid = true;
                }
                catch
                {
                    m_log.Info("[CITY BUILDER]: Unable to create region!");
                    return (false);
                  fine = false;
                    port++;
                    tries-=2;
                }
            }

            if (fine && valid)
            {
               m_log.Info("[CITY BUILDER]: Region created.");
            }
            else
            {
                m_log.Info("[CITY BUILDER]: Failed.");
                return (false);
            }

            region.InternalEndPoint.Port = (int)port;

            //  Construct a new physics thingy for the scene.
            scene.PhysicsScene = new OpenSim.Framework.PhysicsScene();
            //  Obtain links to any current modules installed and tell the new scene about them.
            cityMap.cityRegions[x,y].AddModuleInterfaces(simulationBase.ApplicationRegistry.GetInterfaces());
            //  initialise the scene.
            scene.SceneManager.CreateRegion(region, out iScene);
            cityMap.cityRegions[x,y].Initialize(region, circuitManager, clientServer);
            //  Tell the client server about the new scene.
            clientServer.AddScene(scene);

            //Do this here so that we don't have issues later when startup complete messages start coming in
            m_localScenes.Add(scene);

            m_log.Info("[Modules]: Loading region modules");
            IRegionModulesController controller;
            if (simulationBase.ApplicationRegistry.TryRequestModuleInterface(out controller))
            {
                controller.AddRegionToModules(scene);
            }
            else
                m_log.Error("[Modules]: The new RegionModulesController is missing...");

            //Post init the modules now
            PostInitModules(scene);

            //Start the heartbeats DONT START THE HEARTBEATS!
            scene.StartHeartbeat();
            //Tell the scene that the startup is complete 
            // Note: this event is added in the scene constructor
            cityMap.cityRegions[x,y].FinishedStartup("Startup", new List<string>());

            */

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

            //  Decide where the city is to be placed within the server instance.
            int r = CityModule.randomValue(16);// (int)(27 / 2.45f + (((4 / 5) * 4) / 3));

            string regionCount = MainConsole.Instance.CmdPrompt("Region Count ", r.ToString());
            r = Convert.ToInt32(regionCount);
            m_log.InfoFormat("[CITY BUILDER]: City area {0} region ^2", r * r);

            cityName = MainConsole.Instance.CmdPrompt("City Name ", cityName);
            cityOwner = MainConsole.Instance.CmdPrompt("City Owner ", cityOwner);

            //  Obtain the scene manager, scene graph and region info connector from the server.
            if (simulationBase.Equals(null))
            {
                m_log.Info("[CITYBUILDER]: Unable to continue, no simulation base!");
                return (false);
            }
            //  Obtain the scene manager.
            sceneManager = simulationBase.ApplicationRegistry.RequestModuleInterface<SceneManager>();
            //  Obtain the user account interface for the server.
            //m_UserAccountService = simulationBase.ApplicationRegistry.RegisterModuleInterface<IUserAccountService>();
            //  Obtain the estate/parcel interfaces.

            //  Construct the data instance for a city map to hold the total regions in the simulation.
            cityMap = new CityMap();
            citySeed = seed_value;
            cityMap.cityRegions = new Scene[r, r];
            cityMap.cityPlots = new List<BuildingPlot>();
            cityMap.cityBuildings = new List<CityBuilding>();

            //  Make sure that the user and estate information specified in the configuration file
            // have been loaded and the information has either been found or has been created.

            //  Construct the regions for the city.
            for (rx = 0; rx < r; rx++)
            {
                for (ry = 0; ry < r; ry++)
                {
                    if (!createRegion(rx, ry))
                    {
                        m_log.InfoFormat("[CITY BUILDER]: Failed to construct region at {0},{1}", rx, ry);
                        return (false);
                    }
                    else
                    {
                        m_log.InfoFormat("[CITY BUILDER]: Region created @ {0},{1}", rx, ry);
                    }
                }
            }

            //  Either generate the terrain or loading from an existing file, DEM for example.
            m_log.Info("[CITY BUILDER]: [TERRAIN]");

            //  For each region, just fill the terrain to be 21. This is just above the default
            // water level for Aurora.
            for (rx = 0; rx < r; rx++)
            {
                for (ry = 0; ry < r; ry++)
                {
                    Scene region = cityMap.cityRegions[rx, ry];
                    ITerrainChannel tChannel = null;
                    tChannel = new TerrainChannel(true, region);
                }
            }


            m_log.Info("[CITY BUILDER]: [CENTERS]");
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
                citySeed = CityModule.randomValue(257);
                cityName = cityConfig.GetString("DefaultCityName", "CityVille");
                cityOwner = cityConfig.GetString("DefaultCityOwner", "Cobra ElDiablo");
                CityEstate = cityConfig.GetString("DefaultCityEstate", "Liquid Silicon Developments");
                sceneManager = null;
                cityDensities = new List<float>();
                m_DefaultStartLocation = new Vector2(9500, 9500);
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

            doGenerate(32767);
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
            string owner = MainConsole.Instance.CmdPrompt("Owner", "Cobra ElDiablo");

            doBuilding();

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
