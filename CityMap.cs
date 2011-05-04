/*
 *      This code file deals with asset tracking in the world, namely the regions occupied
 * by the city, along with buildings, plots etc.
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
using OpenSim.Data;
using OpenSim.Region;
using OpenSim.Region.CoreModules;
using OpenSim.Region.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using Aurora.Framework;
using OpenSim.Framework;
using OpenSim;

using Aurora.Modules.CityBuilder.GeoSpatial.DataTypes;

namespace Aurora.Modules.CityBuilder
{
    /// <summary>
    /// Describes a base layer for a city map, contains:
    ///     cityRegion          A link that denotes which region (scene) is being used for this map in the server instance.
    ///     age                 The age of the city (simulated) in years, future to allow for the city to change dynamically with age.
    ///     multiRegion         A flag used to represent whether or not the city is in a single region (scene) or spread over multiple regions.
    ///     cityBuildings       A list of buildings that are within the city limits.
    ///     cityPlots           A list of plots used in the city map, note that a plot can contain other things apart from buildings.
    ///     cityClaims          An array of values indicating the type of claim on the land, part of a complex, transport etc.
    ///     estateIdent         UUID of owning estate.
    ///     cityMapOwner        UUID of owning avatar.
    ///     cityLandData        Parcel settings for all regions contained within the city.
    ///     cityEstateSettings  Estate settings for all regions in the city.
    /// </summary>
    public class CityMap
    {
        /*
         *  This class is about to be changed, moved to it's own file and expanded in order to deal
         *  with cities covering more than one region (regardless of region size).
         */
        #region Private Members

        public List<Scene> centralRegions = new List<Scene>();          // This property can be used to store a list
        // of 'centers', for larger cities, that form the more densely packed regions, difference
        // between the center of London for example and the 'suburbs'. These are considered 'hotzones'
        // where the majority of the taller structures will be placed and buildings are more closely
        // packed together.
        public Scene[,] cityRegions;                //  An array of regions covered by this city.
        public List<BuildingPlot> cityPlots = new List<BuildingPlot>();        //  A list of all the plots in this city.
        public List<CityBuilding> cityBuildings = new List<CityBuilding>();    //  A list of all buildings in this city.
        public int[,] plotArray = null;
        private UUID estateIdent = UUID.Zero;
        private UUID cityMapOwner = UUID.Zero;

        #endregion

        #region Public Members

        //  GET INFO SECTION
        public int GetCentralRegionCount
        {
            get { return centralRegions.Count(); }
        }
        public int GetTotalRegions
        {
            get { return cityRegions.GetUpperBound(0) * cityRegions.GetUpperBound(1); }
        }
        public int GetNumPlots
        {
            get { return cityPlots.Count(); }
        }
        public int GetNumBuildings
        {
            get { return cityBuildings.Count(); }
        }
        public BuildingPlot GetPlot(int idx)
        {
            if (idx < 0 || idx > cityPlots.Count)
            {
                return (null);
            }

            return (cityPlots[idx]);
        }
        public CityBuilding GetBuilding(int idx)
        {
            if (idx < 0 || idx > cityBuildings.Count)
            {
                return (null);
            }
            return (cityBuildings[idx]);
        }
        public CityBuilding GetBuilding(UUID ident)
        {
            if (ident == UUID.Zero)
            {
                return (null);
            }
            foreach (CityBuilding building in cityBuildings)
            {
                if (building.UUID.Equals(ident))
                {
                    return (building);
                }
            }
            return (null);
        }
        public CityBuilding GetBuilding(string buildingName)
        {
            return (null);
        }

        //  PLOT CONTROL

        /// <summary>
        /// Constructs a building plot for a given position, size and claim type, does not
        /// alter any internal properties, this is just a helper method that allows you to
        /// create building plots quickly for use as parameters to other internal/external
        /// methods that the class provides.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="w"></param>
        /// <param name="d"></param>
        /// <param name="flags"></param>
        /// <returns></returns>
        public BuildingPlot MakePlot(int x, int y, int w, int d, PlotClaimType flags)
        {
            BuildingPlot plot = new BuildingPlot();
            plot.xpos = x;
            plot.ypos = y;
            plot.width = (byte)w;
            plot.depth = (byte)d;
            plot.plotFlags = flags;
            return (plot);
        }
        /// <summary>
        /// Finds which building plot is occupying the point of land specified.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public int FindPlot(int x, int y, int w, int d, out PlotClaimType type)
        {
            if (cityPlots.Count <= 0)
            {
                type = PlotClaimType.CLAIM_NONE;
                return (-1);
            }

            int idx = 0;
            foreach (BuildingPlot p in cityPlots)
            {
                if (p.Equals(MakePlot(x, y, w, d,PlotClaimType.CLAIM_BUILDING|PlotClaimType.CLAIM_COMPLEX)))
                {
                    type = p.plotFlags;
                    return (idx);
                }
                idx++;
            }

            type = PlotClaimType.CLAIM_NONE;
            return (-1);
        }
        /// <summary>
        /// Determines if the plot specified has been claimed already or not.
        /// </summary>
        /// <param name="plot"></param>
        /// <returns></returns>
        public bool isPlotClaimed(BuildingPlot plot)
        {
            //  For each entry in the city plots list determine if the plot
            // given as a parameter is allocated to another building, the road
            // network etc.
            foreach (BuildingPlot p in cityPlots)
            {
                if (plot.xpos >= p.xpos && plot.ypos >= p.ypos &&
                    (plot.xpos + plot.width <= p.xpos + p.width) &&
                    (plot.ypos + plot.depth <= p.ypos + p.depth))
                {
                    //  Plot specified is in this plots area. Determine if it has been
                    // claimed for anything other than part of a complex.
                    if (p.plotFlags == PlotClaimType.CLAIM_NONE)
                        return (false);
                }
            }
            //  Defaults to returning true to indicate whether regardless of whether the
            // plot is actually claimed or not, safety fall through.
            return (true);
        }
        /// <summary>
        /// This method will lay claim to a plot of land in the city map. Find the associated 
        /// plot in the internal plot list and claim it, if the plot is not found then add it
        /// to the list.
        /// </summary>
        /// <param name="x">The desired x position of the sw corner of the plot.</param>
        /// <param name="y">The desired y position of the sw corner of the plot.</param>
        /// <param name="width">The desired width of the plot, x+width = x position for ne corner.</param>
        /// <param name="depth">The desired depth of the plot, y+depth = y position for ne corner.</param>
        /// <param name="val">The type of plot this is, ie does it have a building, road, etc on it.</param>
        public bool ClaimPlot(BuildingPlot plot)
        {
            if (plot.Equals(null))
                return (false);

            if (isPlotClaimed(plot))
                return (false);

            //  Search the list.
            if (cityPlots.Count > 0)
            {
                foreach (BuildingPlot p in cityPlots)
                {
                    if (p.Equals(plot))
                    {
                        if (p.plotFlags == PlotClaimType.CLAIM_NONE)
                            p.plotFlags = PlotClaimType.CLAIM_PARK;
                        return (true);
                    }
                }
            }

            //  If we are here then the plot has not been found so add it as new plot.
            cityPlots.Add(plot);

            return (true);
        }

        //  SCENE CONTROL.
        public bool AddScene(Scene scene, bool central)
        {
            if (scene.Equals(null))
                return (false);

            // do it the hard way and find the first available space.
            if (cityRegions.Equals(null))
                return (false);
            if (cityRegions.GetUpperBound(0) == 0 || cityRegions.GetUpperBound(1) == 0)
            {
                CityModule.m_log.Info("[CITY BUILDER]: No space in city regions!");
                return (false);
            }
            for (int rx = 0; rx < cityRegions.GetUpperBound(0); rx++)
            {
                for (int ry = 0; ry < cityRegions.GetUpperBound(1); ry++)
                {
                    if (cityRegions[rx, ry].Equals(scene))
                        return (true);
                    if (cityRegions[rx, ry].Equals(null))
                    {
                        cityRegions[rx, ry] = scene;
                        if (central)
                            centralRegions.Add(scene);
                        CityModule.m_log.InfoFormat("[CITY BUILDER]: Added new region {0} @ {1},{2}",
                            scene.RegionInfo.RegionName, rx, ry);
                        return (true);
                    }
                }
            }

            return (false);
        }
        public bool RemoveScene(Scene scene)
        {
            //  Remove the given scene from the city map
            if (scene.Equals(null))
                return (false);

            //  Firstly check to see if the region is part of one of the central regions.
            for (int c = 0; c < centralRegions.Count(); c++)
            {
                Scene s = centralRegions[c];
                if (s.Equals(scene))
                {
                    centralRegions.Remove(s);
                    break;
                }
            }

            //  Now scan the entire map and remove it.
            for (int rx = 0; rx < cityRegions.GetUpperBound(0); rx++)
            {
                for (int ry = 0; rx < cityRegions.GetUpperBound(1); ry++)
                {
                    Scene r = cityRegions[rx, ry];
                    if (r.Equals(scene))
                    {
                        cityRegions[rx, ry] = null;
                        return (true);
                    }
                }
            }

            return (false);
        }
        public Scene this[int rx, int ry]
        {
            get
            {
                if (rx < 0 || ry < 0 || rx > cityRegions.GetUpperBound(0) || ry > cityRegions.GetUpperBound(1))
                {
                    return (null);
                }
                return (cityRegions[rx, ry]);
            }
            set
            {
                if (rx < 0 || ry < 0 || rx > cityRegions.GetUpperBound(0) || ry > cityRegions.GetUpperBound(1))
                {
                    return;
                }
                if (value == null)
                    return;
                cityRegions[rx,ry] = value;
            }
        }

        #endregion

        #region Constructors

        public CityMap()
        {
            cityRegions = new Scene[0,0];
            plotArray = new int[0, 0];
        }

        public CityMap(uint regionCount, UUID estateOwner, UUID avatar, List<Scene> centers,
            List<float> regionDensities, uint mapSeed)
        {
            //  Force the size of the city to be at least 1 region in size. If a list of regions
            // has been provided then make sure it is at least that long.
            if (regionCount <= 0)
            {
//                if (centralRegions.Count > 1)
//                    regionCount = centers.Count;
//                else
                    regionCount = 1;
            }

            //  Allocate the space required for the number of regions specified.
            cityRegions = new Scene[regionCount, regionCount];
            plotArray = new int[regionCount, regionCount];

            if ( centralRegions.Capacity > 0 )
            {
                centralRegions.Clear();
            }

            centralRegions = new List<Scene>();
            foreach (Scene s in centers)
            {
                centralRegions.Add(s);
            }

        }

        /*
        CityMap(Scene scene)
        {
            //  Construct this instance from the given scene, does not change any of the
            // plots or buildings if they are defined.
            if (scene != null)
            {
                int rxo = scene.RegionInfo.RegionLocX / cityRegions.GetUpperBound(0);
                int ryo = scene.RegionInfo.RegionLocY / cityRegions.GetUpperBound(1);

                //  Is the region rxo,ryo occupied?
                if (cityRegions[rxo, ryo].Equals(null))
                {
                    cityRegions[rxo, ryo] = scene;
                }
                else
                {
                    //  Find the first available region within the map.
                    for (int rx = 0; rx < cityRegions.GetUpperBound(0); rx++)
                    {
                        for (int ry = 0; ry < cityRegions.GetUpperBound(1); ry++)
                        {
                            if (cityRegions[rx, ry].Equals(null))
                            {
                                cityRegions[rx, ry] = scene;
                                break;
                            }
                        }
                    }
                }

                //  Now add this scene as a central scene, the reason being is that if
                // we are being constructed from a given scene then the map must not
                // exist so therefore the first scene will always be the central region
                // for a city regardless of how big the whole city is.
                centralRegions.Add(scene);
            }
        }
        */

        #endregion
    };
}
