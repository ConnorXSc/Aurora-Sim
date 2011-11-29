/*
 * Copyright (c) Contributors, http://aurora-sim.org/, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the Aurora-Sim Project nor the
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

using System.IO;
using System.Xml.Serialization;
using Aurora.Simulation.Base;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;

namespace OpenSim.Services
{
    public class AssetServerDeleteHandler : BaseStreamHandler
    {
        // private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly IAssetService m_AssetService;
        protected string m_SessionID;
        protected bool m_allowDelete;
        protected IRegistryCore m_registry;

        public AssetServerDeleteHandler(IAssetService service, bool allowDelete, string url, string SessionID,
                                        IRegistryCore registry) :
                                            base("DELETE", url)
        {
            m_AssetService = service;
            m_allowDelete = allowDelete;
            m_SessionID = SessionID;
            m_registry = registry;
        }

        public override byte[] Handle(string path, Stream request,
                                      OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            bool result = false;

            string[] p = SplitParams(path);

            IGridRegistrationService urlModule =
                m_registry.RequestModuleInterface<IGridRegistrationService>();
            if (m_SessionID != "" && urlModule != null)
                if (!urlModule.CheckThreatLevel(m_SessionID, "Asset_Delete", ThreatLevel.Full))
                    return new byte[0];
            if (p.Length > 0 && m_allowDelete)
            {
                result = m_AssetService.Delete(UUID.Parse(p[0]));
            }

            XmlSerializer xs = new XmlSerializer(typeof (bool));
            return WebUtils.SerializeResult(xs, result);
        }
    }
}