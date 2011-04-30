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
//  This needs to be changed to Aurora.CoreApplicationPlugins, it was working
// but now has revereted to not recognising the namespace despite having added
// the dll as a reference and the project itself as a dependancy of City Builder.
//using OpenSim.CoreApplicationPlugins;
using OpenSim.Framework;

namespace Aurora.Modules.City
{
    /// <summary>
    /// This class deals with the textures used for buildings, roads etc.
    /// </summary>
    public class CityTexture : SceneObjectPart
    {
        #region Internal Properties
        private UUID textureID;
        private UUID ownerID;
        private Vector2 textureSize;
        private string textureName;
        #endregion
        #region Internal Methods
        #endregion
        #region Public Properties
        public UUID TextureID
        {
            get { return textureID; }
        }
        #endregion
        #region Public Methods
        /// <summary>
        /// Register a new texture with the asset system for use by buildings, roads etc.
        /// The texture object is owned by the server ie uuid zero.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="texture_id"></param>
        /// <returns></returns>
        public UUID registerNewTexture(string filePath)
        {
            if (filePath.Length <= 0)
                return (UUID.Zero);
            return (UUID.Zero);
        }
        #endregion
        #region Constructors
        #endregion
    }
}
