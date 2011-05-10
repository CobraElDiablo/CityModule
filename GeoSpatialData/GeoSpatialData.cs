using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using Aurora.Framework;

namespace Aurora.Modules.CityBuilder
{
    /// <summary>
    /// Defines the type of land coverage an individual sample is, things like rock, sand, but also urban.
    /// </summary>
    public enum GeoLandCoverType : int
    {
        GEOCOVER_UNKNOWN = -1,
        GEOCOVER_WATER,
        GEOCOVER_COUNT
    };

    //  GeoSpatial data sets.
    namespace GeoSpatial
    {
        //  Base data types.
        namespace DataTypes
        {
            /// <summary>
            /// Defines a single point that is contained within the data set, these usually refer to things
            /// like landmarks and other places of interest.
            /// </summary>
            [Serializable]
            public class GeoPoint : IDataTransferable
            {
                #region Internal Properties
                private Vector3 m_Point;
                #endregion
                #region External Properties
                public Vector3 Point
                {
                    get { return (m_Point); }
                    set { m_Point = value; }
                }
                #endregion
                #region IDataTransferable Interface
                public override IDataTransferable Duplicate()
                {
                    GeoPoint p = new GeoPoint();
                    p.m_Point = m_Point;
                    return (IDataTransferable)p;
                }
                public override void FromKVP(Dictionary<string, object> KVP)
                {
                    base.FromKVP(KVP);
                }
                public override Dictionary<string, object> ToKeyValuePairs()
                {
                    Dictionary<string, object> dict = base.ToKeyValuePairs();
                    dict.Add("Point", m_Point);
                    return dict;
                }
                public override OSDMap ToOSD()
                {
                    OSDMap map = new OSDMap();
                    map = base.ToOSD();
                    map.Add("Point", m_Point);
                    return map;
                }
                public override void FromOSD(OSDMap map)
                {
                    m_Point = map.AsVector3();
                    base.FromOSD(map);
                }
                #endregion
            }
            /// <summary>
            /// Definition of an edge, these are usually used for boundaries between land and water.
            /// </summary>
            public class GeoEdge : IDataTransferable
            {
                private GeoPoint m_StartPoint;
                private GeoPoint m_EndPoint;

                public GeoPoint StartPoint
                {
                    get { return (m_StartPoint); }
                    set { m_StartPoint = value; }
                }

                public GeoPoint EndPoint
                {
                    get { return (m_EndPoint); }
                    set { m_EndPoint = value; }
                }

                public float Distance
                {
                    get
                    {
                        float dist = new Vector3(m_EndPoint.Point - m_StartPoint.Point).Length();
                        return (dist);
                    }
                }

                public override IDataTransferable Duplicate()
                {
                    return base.Duplicate();
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
                    base.FromOSD(map);
                }

                public override OSDMap ToOSD()
                {
                    OSDMap map = new OSDMap();
                    map = base.ToOSD();
                    map.Add("StartPoint", m_StartPoint.ToString());
                    map.Add("EndPoint", m_EndPoint.ToString());
                    return (map);
                }


            }
            /// <summary>
            /// Defines an area within the data set, this would be used to define things like lakes, parts
            /// or other enclosed areas.
            /// </summary>
            public class GeoPolygon : IDataTransferable
            {
                private GeoPoint m_StartPoint = new GeoPoint();
                private List<GeoEdge> m_Edges = new List<GeoEdge>();

                public GeoPoint StartPoint
                {
                    get
                    {
                        m_StartPoint = m_Edges[0].StartPoint;
                        return (m_StartPoint);
                    }
                    set
                    {
                        m_StartPoint = value;
                        m_Edges[0].StartPoint = value;
                    }
                }

                public GeoEdge this[int index]
                {
                    get
                    {
                        if (m_Edges == null || m_Edges.Count == 0)
                            return (null);
                        if (index < 0 || index > m_Edges.Count)
                            return (null);
                        return (m_Edges[index]);
                    }
                    set
                    {
                        if (index < 0 || index > m_Edges.Count)
                        {
                            return;
                        }
                        if (index == 0)
                        {
                            m_StartPoint = value.StartPoint;
                        }
                        m_Edges[index] = value;
                    }
                }

                public float PolygonArea
                {
                    get
                    {
                        float area = 0.0f;
                        if (m_Edges == null || m_Edges.Count <= 0)
                        {
                            return (area);
                        }
                        int i, j;
                        for (i = 0; i < m_Edges.Count; i++)
                        {
                            j = (i + 1) % m_Edges.Count;
                            area += m_Edges[i].StartPoint.Point.X * m_Edges[i].StartPoint.Point.Y;
                            area -= m_Edges[i].StartPoint.Point.Y * m_Edges[i].StartPoint.Point.X;
                        }
                        area /= 2;
                        return (area < 0 ? -area : area);
                    }
                }

                public override IDataTransferable Duplicate()
                {
                    return base.Duplicate();
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
                    base.FromOSD(map);
                }
                public override OSDMap ToOSD()
                {
                    return base.ToOSD();
                }
            }

        }

    }
}
