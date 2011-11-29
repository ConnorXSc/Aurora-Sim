/*
 * Copyright (c) Contributors, http://aurora-sim.org/
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Aurora.Framework;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Serialization.External;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Services.Interfaces;
using log4net;

namespace Aurora.Modules
{
    /// <summary>
    ///   This module loads/saves the avatar's appearance from/down into an "Avatar Archive", also known as an AA.
    /// </summary>
    public class AuroraAvatarAppearanceArchiver : ISharedRegionModule, IAvatarAppearanceArchiver
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private IAssetService AssetService;
        private IAvatarService AvatarService;
        private IInventoryService InventoryService;
        private IUserAccountService UserAccountService;
        private IScene m_scene;

        public bool IsSharedModule
        {
            get { return true; }
        }

        #region IAvatarAppearanceArchiver Members

        public void LoadAvatarArchive(string FileName, string Name)
        {
            UserAccount account = UserAccountService.GetUserAccount(UUID.Zero, Name);
            m_log.Info("[AvatarArchive] Loading archive from " + FileName);
            if (account == null)
            {
                m_log.Error("[AvatarArchive] User not found!");
                return;
            }

            StreamReader reader = new StreamReader(FileName);
            string file = reader.ReadToEnd();
            reader.Close();
            reader.Dispose();

            IScenePresence SP;
            m_scene.TryGetScenePresence(account.PrincipalID, out SP);
            if (SP == null)
                return; //Bad people!

            SP.ControllingClient.SendAlertMessage("Appearance loading in progress...");

            string FolderNameToLoadInto = "";

            OSDMap map = ((OSDMap) OSDParser.DeserializeLLSDXml(file));

            OSDMap assetsMap = ((OSDMap) map["Assets"]);
            OSDMap itemsMap = ((OSDMap) map["Items"]);
            OSDMap bodyMap = ((OSDMap) map["Body"]);

            AvatarAppearance appearance = ConvertXMLToAvatarAppearance(bodyMap, out FolderNameToLoadInto);

            appearance.Owner = account.PrincipalID;

            List<InventoryItemBase> items = new List<InventoryItemBase>();

            InventoryFolderBase AppearanceFolder = InventoryService.GetFolderForType(account.PrincipalID,
                                                                                     InventoryType.Wearable,
                                                                                     AssetType.Clothing);

            InventoryFolderBase folderForAppearance
                = new InventoryFolderBase(
                    UUID.Random(), FolderNameToLoadInto, account.PrincipalID,
                    -1, AppearanceFolder.ID, 1);

            InventoryService.AddFolder(folderForAppearance);

            folderForAppearance = InventoryService.GetFolder(folderForAppearance);

            try
            {
                LoadAssets(assetsMap);
                LoadItems(itemsMap, account.PrincipalID, folderForAppearance, out items);
            }
            catch (Exception ex)
            {
                m_log.Warn("[AvatarArchiver]: Error loading assets and items, " + ex);
            }

            //Now update the client about the new items
            SP.ControllingClient.SendBulkUpdateInventory(folderForAppearance);
            foreach (InventoryItemBase itemCopy in items)
            {
                if (itemCopy == null)
                {
                    SP.ControllingClient.SendAgentAlertMessage("Can't find item to give. Nothing given.", false);
                    continue;
                }
                if (!SP.IsChildAgent)
                {
                    SP.ControllingClient.SendBulkUpdateInventory(itemCopy);
                }
            }
            m_log.Info("[AvatarArchive] Loaded archive from " + FileName);
        }

        #endregion

        #region ISharedRegionModule Members

        public void Initialise(IConfigSource source)
        {
        }

        public void AddRegion(IScene scene)
        {
            if (m_scene == null)
                m_scene = scene;

            if (MainConsole.Instance != null)
            {
                MainConsole.Instance.Commands.AddCommand("save avatar archive",
                                                         "save avatar archive <First> <Last> <Filename> <FolderNameToSaveInto>",
                                                         "Saves appearance to an avatar archive archive (Note: put \"\" around the FolderName if you need more than one word. Put all attachments in BodyParts folder before saving the archive)",
                                                         HandleSaveAvatarArchive);
                MainConsole.Instance.Commands.AddCommand("load avatar archive",
                                                         "load avatar archive <First> <Last> <Filename>",
                                                         "Loads appearance from an avatar archive archive",
                                                         HandleLoadAvatarArchive);
            }
        }

        public void RemoveRegion(IScene scene)
        {
        }

        public void RegionLoaded(IScene scene)
        {
            InventoryService = m_scene.InventoryService;
            AssetService = m_scene.AssetService;
            UserAccountService = m_scene.UserAccountService;
            AvatarService = m_scene.AvatarService;
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public string Name
        {
            get { return "AuroraAvatarArchiver"; }
        }

        #endregion

        protected void HandleLoadAvatarArchive(string[] cmdparams)
        {
            if (cmdparams.Length != 6)
            {
                m_log.Info("[AvatarArchive] Not enough parameters!");
                return;
            }
            LoadAvatarArchive(cmdparams[5], cmdparams[3] + " " + cmdparams[4]);
        }

        private InventoryItemBase GiveInventoryItem(UUID senderId, UUID recipient, InventoryItemBase item,
                                                    InventoryFolderBase parentFolder)
        {
            InventoryItemBase itemCopy = new InventoryItemBase
                                             {
                                                 Owner = recipient,
                                                 CreatorId = item.CreatorId,
                                                 CreatorData = item.CreatorData,
                                                 ID = UUID.Random(),
                                                 AssetID = item.AssetID,
                                                 Description = item.Description,
                                                 Name = item.Name,
                                                 AssetType = item.AssetType,
                                                 InvType = item.InvType,
                                                 Folder = UUID.Zero,
                                                 NextPermissions = (uint) PermissionMask.All,
                                                 GroupPermissions = (uint) PermissionMask.All,
                                                 EveryOnePermissions = (uint) PermissionMask.All,
                                                 CurrentPermissions = (uint) PermissionMask.All
                                             };

            //Give full permissions for them

            if (parentFolder == null)
            {
                InventoryFolderBase folder = InventoryService.GetFolderForType(recipient, InventoryType.Unknown,
                                                                               (AssetType) itemCopy.AssetType);

                if (folder != null)
                    itemCopy.Folder = folder.ID;
                else
                {
                    InventoryFolderBase root = InventoryService.GetRootFolder(recipient);

                    if (root != null)
                        itemCopy.Folder = root.ID;
                    else
                        return null; // No destination
                }
            }
            else
                itemCopy.Folder = parentFolder.ID; //We already have a folder to put it in

            itemCopy.GroupID = UUID.Zero;
            itemCopy.GroupOwned = false;
            itemCopy.Flags = item.Flags;
            itemCopy.SalePrice = item.SalePrice;
            itemCopy.SaleType = item.SaleType;

            InventoryService.AddItem(itemCopy);
            return itemCopy;
        }

        private AvatarAppearance ConvertXMLToAvatarAppearance(OSDMap map, out string FolderNameToPlaceAppearanceIn)
        {
            AvatarAppearance appearance = new AvatarAppearance();
            appearance.Unpack(map);
            FolderNameToPlaceAppearanceIn = map["FolderName"].AsString();
            return appearance;
        }

        protected void HandleSaveAvatarArchive(string[] cmdparams)
        {
            if (cmdparams.Length != 7)
            {
                m_log.Info("[AvatarArchive] Not enough parameters!");
            }
            UserAccount account = UserAccountService.GetUserAccount(UUID.Zero, cmdparams[3] + " " + cmdparams[4]);
            if (account == null)
            {
                m_log.Error("[AvatarArchive] User not found!");
                return;
            }

            IScenePresence SP;
            m_scene.TryGetScenePresence(account.PrincipalID, out SP);
            if (SP == null)
                return; //Bad people!
            SP.ControllingClient.SendAlertMessage("Appearance saving in progress...");

            AvatarAppearance appearance = AvatarService.GetAppearance(SP.UUID);
            if (appearance == null)
            {
                IAvatarAppearanceModule appearancemod = SP.RequestModuleInterface<IAvatarAppearanceModule>();
                appearance = appearancemod.Appearance;
            }
            StreamWriter writer = new StreamWriter(cmdparams[5], false);
            OSDMap map = new OSDMap();
            OSDMap body = new OSDMap();
            OSDMap assets = new OSDMap();
            OSDMap items = new OSDMap();
            body = appearance.Pack();
            body.Add("FolderName", OSD.FromString(cmdparams[6]));

            foreach (AvatarWearable wear in appearance.Wearables)
            {
                for (int i = 0; i < wear.Count; i++)
                {
                    WearableItem w = wear[i];
                    if (w.AssetID != UUID.Zero)
                    {
                        SaveItem(w.ItemID, items, assets);
                        SaveAsset(w.AssetID, assets);
                    }
                }
            }
            List<AvatarAttachment> attachments = appearance.GetAttachments();
            foreach (AvatarAttachment a in attachments.Where(a => a.AssetID != UUID.Zero))
            {
                SaveItem(a.ItemID, items, assets);
                SaveAsset(a.AssetID, assets);
            }
            map.Add("Body", body);
            map.Add("Assets", assets);
            map.Add("Items", items);
            //Write the map
            writer.Write(OSDParser.SerializeLLSDXmlString(map));
            writer.Close();
            writer.Dispose();
            m_log.Info("[AvatarArchive] Saved archive to " + cmdparams[5]);
        }

        private void SaveAsset(UUID AssetID, OSDMap assetMap)
        {
            try
            {
                AssetBase asset = AssetService.Get(AssetID.ToString());
                if (asset != null)
                {
                    OSDMap assetData = new OSDMap();
                    m_log.Info("[AvatarArchive]: Saving asset " + asset.ID);
                    CreateMetaDataMap(asset, assetData);
                    assetData.Add("AssetData", OSD.FromBinary(asset.Data));
                    assetMap.Add(asset.ID.ToString(), assetData);
                }
                else
                {
                    m_log.Warn("[AvatarArchive]: Could not find asset to save: " + AssetID.ToString());
                    return;
                }
            }
            catch (Exception ex)
            {
                m_log.Warn("[AvatarArchive]: Could not save asset: " + AssetID.ToString() + ", " + ex);
            }
        }

        private void CreateMetaDataMap(AssetBase data, OSDMap map)
        {
            map["ContentType"] = OSD.FromString(data.TypeString);
            map["CreationDate"] = OSD.FromDate(data.CreationDate);
            map["CreatorID"] = OSD.FromUUID(data.CreatorID);
            map["Description"] = OSD.FromString(data.Description);
            map["ID"] = OSD.FromUUID(data.ID);
            map["Name"] = OSD.FromString(data.Name);
            map["Type"] = OSD.FromInteger(data.Type);
        }

        private AssetBase LoadAssetBase(OSDMap map)
        {
            AssetBase asset = new AssetBase
                                  {
                                      Data = map["AssetData"].AsBinary(),
                                      TypeString = map["ContentType"].AsString(),
                                      CreationDate = map["CreationDate"].AsDate(),
                                      CreatorID = map["CreatorID"].AsUUID(),
                                      Description = map["Description"].AsString(),
                                      ID = map["ID"].AsUUID(),
                                      Name = map["Name"].AsString(),
                                      Type = (sbyte) map["Type"].AsInteger()
                                  };
            return asset;
        }

        private void SaveItem(UUID ItemID, OSDMap itemMap, OSDMap assets)
        {
            InventoryItemBase saveItem = InventoryService.GetItem(new InventoryItemBase(ItemID));
            if (saveItem == null)
            {
                m_log.Warn("[AvatarArchive]: Could not find item to save: " + ItemID.ToString());
                return;
            }
            m_log.Info("[AvatarArchive]: Saving item " + ItemID.ToString());
            string serialization = UserInventoryItemSerializer.Serialize(saveItem);
            itemMap[ItemID.ToString()] = OSD.FromString(serialization);
        }

        private void LoadAssets(OSDMap assets)
        {
            foreach (KeyValuePair<string, OSD> kvp in assets)
            {
                UUID AssetID = UUID.Parse(kvp.Key);
                OSDMap assetMap = (OSDMap) kvp.Value;
                AssetBase asset = AssetService.Get(AssetID.ToString());
                m_log.Info("[AvatarArchive]: Loading asset " + AssetID.ToString());
                if (asset == null) //Don't overwrite
                {
                    asset = LoadAssetBase(assetMap);
                    asset.ID = AssetService.Store(asset);
                }
            }
        }

        private void LoadItems(OSDMap items, UUID OwnerID, InventoryFolderBase folderForAppearance,
                               out List<InventoryItemBase> litems)
        {
            litems = new List<InventoryItemBase>();
            foreach (KeyValuePair<string, OSD> kvp in items)
            {
                string serialization = kvp.Value.AsString();
                InventoryItemBase item = UserInventoryItemSerializer.Deserialize(serialization);
                m_log.Info("[AvatarArchive]: Loading item " + item.ID.ToString());
                item = GiveInventoryItem(item.CreatorIdAsUuid, OwnerID, item, folderForAppearance);
                litems.Add(item);
            }
        }
    }
}