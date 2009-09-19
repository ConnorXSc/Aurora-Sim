/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using Nini.Config;
using log4net;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Data;
using OpenSim.Services.Interfaces;
using OpenMetaverse;

namespace OpenSim.Services.GridService
{
    public class GridService : GridServiceBase, IGridService
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        public GridService(IConfigSource config)
            : base(config)
        {
            MainConsole.Instance.Commands.AddCommand("kfs", false,
                    "show digest",
                    "show digest <ID>",
                    "Show asset digest", HandleShowDigest);

            MainConsole.Instance.Commands.AddCommand("kfs", false,
                    "delete asset",
                    "delete asset <ID>",
                    "Delete asset from database", HandleDeleteAsset);

        }

        #region IGridService

        public bool RegisterRegion(UUID scopeID, SimpleRegionInfo regionInfos)
        {
            if (m_Database.Get(regionInfos.RegionID, scopeID) != null)
            {
                m_log.WarnFormat("[GRID SERVICE]: Region {0} already registered in scope {1}.", regionInfos.RegionID, scopeID);
                return false;
            }
            if (m_Database.Get((int)regionInfos.RegionLocX, (int)regionInfos.RegionLocY, scopeID) != null)
            {
                m_log.WarnFormat("[GRID SERVICE]: Region {0} tried to register in coordinates {1}, {2} which are already in use in scope {3}.", 
                    regionInfos.RegionID, regionInfos.RegionLocX, regionInfos.RegionLocY, scopeID);
                return false;
            }

            // Everything is ok, let's register
            RegionData rdata = RegionInfo2RegionData(regionInfos);
            rdata.ScopeID = scopeID;
            m_Database.Store(rdata);
            return true;
        }

        public bool DeregisterRegion(UUID regionID)
        {
            return m_Database.Delete(regionID);
        }

        public List<SimpleRegionInfo> GetNeighbours(UUID scopeID, int x, int y)
        {
            List<RegionData> rdatas = m_Database.Get(x - 1, y - 1, x + 1, y + 1, scopeID);
            List<SimpleRegionInfo> rinfos = new List<SimpleRegionInfo>();
            foreach (RegionData rdata in rdatas)
                rinfos.Add(RegionData2RegionInfo(rdata));

            return rinfos;
        }

        public SimpleRegionInfo GetRegionByUUID(UUID scopeID, UUID regionID)
        {
            RegionData rdata = m_Database.Get(regionID, scopeID);
            if (rdata != null)
                return RegionData2RegionInfo(rdata);

            return null;
        }

        public SimpleRegionInfo GetRegionByPosition(UUID scopeID, int x, int y)
        {
            RegionData rdata = m_Database.Get(x, y, scopeID);
            if (rdata != null)
                return RegionData2RegionInfo(rdata);

            return null;
        }

        public SimpleRegionInfo GetRegionByName(UUID scopeID, string regionName)
        {
            List<RegionData> rdatas = m_Database.Get(regionName + "%", scopeID);
            if ((rdatas != null) && (rdatas.Count > 0))
                return RegionData2RegionInfo(rdatas[0]); // get the first

            return null;
        }

        public List<SimpleRegionInfo> GetRegionsByName(UUID scopeID, string name, int maxNumber)
        {
            List<RegionData> rdatas = m_Database.Get("%" + name + "%", scopeID);

            int count = 0;
            List<SimpleRegionInfo> rinfos = new List<SimpleRegionInfo>();

            if (rdatas != null)
            {
                foreach (RegionData rdata in rdatas)
                {
                    if (count++ < maxNumber)
                        rinfos.Add(RegionData2RegionInfo(rdata));
                }
            }

            return rinfos;
        }

        public List<SimpleRegionInfo> GetRegionRange(UUID scopeID, int xmin, int xmax, int ymin, int ymax)
        {
            List<RegionData> rdatas = m_Database.Get(xmin, ymin, xmax, ymax, scopeID);
            List<SimpleRegionInfo> rinfos = new List<SimpleRegionInfo>();
            foreach (RegionData rdata in rdatas)
                rinfos.Add(RegionData2RegionInfo(rdata));

            return rinfos;
        }

        #endregion

        #region Data structure conversions

        protected RegionData RegionInfo2RegionData(SimpleRegionInfo rinfo)
        {
            RegionData rdata = new RegionData();
            rdata.posX = (int)rinfo.RegionLocX;
            rdata.posY = (int)rinfo.RegionLocY;
            rdata.RegionID = rinfo.RegionID;
            //rdata.RegionName = rinfo.RegionName;
            rdata.Data["external_ip_address"] = rinfo.ExternalEndPoint.Address.ToString();
            rdata.Data["external_port"] = rinfo.ExternalEndPoint.Port.ToString();
            rdata.Data["external_host_name"] = rinfo.ExternalHostName;
            rdata.Data["http_port"] = rinfo.HttpPort.ToString();
            rdata.Data["internal_ip_address"] = rinfo.InternalEndPoint.Address.ToString();
            rdata.Data["internal_port"] = rinfo.InternalEndPoint.Port.ToString();
            rdata.Data["alternate_ports"] = rinfo.m_allow_alternate_ports.ToString();
            rdata.Data["server_uri"] = rinfo.ServerURI;

            return rdata;
        }

        protected SimpleRegionInfo RegionData2RegionInfo(RegionData rdata)
        {
            SimpleRegionInfo rinfo = new SimpleRegionInfo();
            rinfo.RegionLocX = (uint)rdata.posX;
            rinfo.RegionLocY = (uint)rdata.posY;
            rinfo.RegionID = rdata.RegionID;
            //rinfo.RegionName = rdata.RegionName;

            // Now for the variable data
            if ((rdata.Data["external_ip_address"] != null) && (rdata.Data["external_port"] != null))
            {
                int port = 0;
                Int32.TryParse((string)rdata.Data["external_port"], out port);
                IPEndPoint ep = new IPEndPoint(IPAddress.Parse((string)rdata.Data["external_ip_address"]), port);
                rinfo.ExternalEndPoint = ep;
            }
            else
                rinfo.ExternalEndPoint = new IPEndPoint(new IPAddress(0), 0);

            if (rdata.Data["external_host_name"] != null)
                rinfo.ExternalHostName = (string)rdata.Data["external_host_name"] ;

            if (rdata.Data["http_port"] != null)
            {
                UInt32 port = 0;
                UInt32.TryParse((string)rdata.Data["http_port"], out port);
                rinfo.HttpPort = port;
            }

            if ((rdata.Data["internal_ip_address"] != null) && (rdata.Data["internal_port"] != null))
            {
                int port = 0;
                Int32.TryParse((string)rdata.Data["internal_port"], out port);
                IPEndPoint ep = new IPEndPoint(IPAddress.Parse((string)rdata.Data["internal_ip_address"]), port);
                rinfo.InternalEndPoint = ep;
            }
            else
                rinfo.InternalEndPoint = new IPEndPoint(new IPAddress(0), 0);

            if (rdata.Data["alternate_ports"] != null)
            {
                bool alts = false;
                Boolean.TryParse((string)rdata.Data["alternate_ports"], out alts);
                rinfo.m_allow_alternate_ports = alts;
            }

            if (rdata.Data["server_uri"] != null)
                rinfo.ServerURI = (string)rdata.Data["server_uri"];

            return rinfo;
        }

        #endregion 

        void HandleShowDigest(string module, string[] args)
        {
            //if (args.Length < 3)
            //{
            //    MainConsole.Instance.Output("Syntax: show digest <ID>");
            //    return;
            //}

            //AssetBase asset = Get(args[2]);

            //if (asset == null || asset.Data.Length == 0)
            //{   
            //    MainConsole.Instance.Output("Asset not found");
            //    return;
            //}

            //int i;

            //MainConsole.Instance.Output(String.Format("Name: {0}", asset.Name));
            //MainConsole.Instance.Output(String.Format("Description: {0}", asset.Description));
            //MainConsole.Instance.Output(String.Format("Type: {0}", asset.Type));
            //MainConsole.Instance.Output(String.Format("Content-type: {0}", asset.Metadata.ContentType));

            //for (i = 0 ; i < 5 ; i++)
            //{
            //    int off = i * 16;
            //    if (asset.Data.Length <= off)
            //        break;
            //    int len = 16;
            //    if (asset.Data.Length < off + len)
            //        len = asset.Data.Length - off;

            //    byte[] line = new byte[len];
            //    Array.Copy(asset.Data, off, line, 0, len);

            //    string text = BitConverter.ToString(line);
            //    MainConsole.Instance.Output(String.Format("{0:x4}: {1}", off, text));
            //}
        }

        void HandleDeleteAsset(string module, string[] args)
        {
            //if (args.Length < 3)
            //{
            //    MainConsole.Instance.Output("Syntax: delete asset <ID>");
            //    return;
            //}

            //AssetBase asset = Get(args[2]);

            //if (asset == null || asset.Data.Length == 0)
            //    MainConsole.Instance.Output("Asset not found");
            //    return;
            //}

            //Delete(args[2]);

            ////MainConsole.Instance.Output("Asset deleted");
            //// TODO: Implement this

            //MainConsole.Instance.Output("Asset deletion not supported by database");
        }
    }
}
