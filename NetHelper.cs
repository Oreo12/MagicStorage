using System;
using System.IO;
using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using MagicStorage.Components;

namespace MagicStorage
{
	public static class NetHelper
	{
		public static void HandlePacket(BinaryReader reader, int sender)
		{
			MessageType type = (MessageType)reader.ReadByte();
			if (type == MessageType.SearchAndRefreshNetwork)
			{
				ReceiveSearchAndRefresh(reader);
			}
			else if (type == MessageType.TryStorageOperation)
			{
				ReceiveStorageOperation(reader, sender);
			}
			else if (type == MessageType.StorageOperationResult)
			{
				ReceiveOperationResult(reader);
			}
			else if (type == MessageType.RefreshNetworkItems)
			{
				StorageGUI.RefreshItems();
			}
			else if (type == MessageType.ClientSendTEUpdate)
			{
				ReceiveClientSendTEUpdate(reader, sender);
			}
		}

		public static void SendComponentPlace(int i, int j, int type)
		{
			if (Main.netMode == 1)
			{
				NetMessage.SendTileRange(Main.myPlayer, i, j, 2, 2);
				NetMessage.SendData(MessageID.TileEntityPlacement, -1, -1, "", i, j, type);
			}
		}

		public static void SendTEUpdate(int id, Point16 position)
		{
			if (Main.netMode == 2)
			{
				NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, "", id, position.X, position.Y);
			}
		}

		public static void SendSearchAndRefresh(int i, int j)
		{
			if (Main.netMode == 1)
			{
				ModPacket packet = MagicStorage.Instance.GetPacket();
				packet.Write((byte)MessageType.SearchAndRefreshNetwork);
				packet.Write((short)i);
				packet.Write((short)j);
				packet.Send();
			}
		}

		private static void ReceiveSearchAndRefresh(BinaryReader reader)
		{
			Point16 point = new Point16(reader.ReadInt16(), reader.ReadInt16());
			TEStorageComponent.SearchAndRefreshNetwork(point);
		}

		private static ModPacket PrepareStorageOperation(int ent, byte op)
		{
			ModPacket packet = MagicStorage.Instance.GetPacket();
			packet.Write((byte)MessageType.TryStorageOperation);
			packet.Write(ent);
			packet.Write(op);
			return packet;
		}

		private static ModPacket PrepareOperationResult(byte op)
		{
			ModPacket packet = MagicStorage.Instance.GetPacket();
			packet.Write((byte)MessageType.StorageOperationResult);
			packet.Write(op);
			return packet;
		}

		public static void SendDeposit(int ent, Item item)
		{
			if (Main.netMode == 1)
			{
				ModPacket packet = PrepareStorageOperation(ent, 0);
				ItemIO.Send(item, packet, true);
				packet.Send();
			}
		}

		public static void SendWithdraw(int ent, Item item, bool toInventory = false)
		{
			if (Main.netMode == 1)
			{
				ModPacket packet = PrepareStorageOperation(ent, (byte)(toInventory ? 3 : 1));
				ItemIO.Send(item, packet, true);
				packet.Send();
			}
		}

		public static void SendDepositAll(int ent, List<Item> items)
		{
			if (Main.netMode == 1)
			{
				ModPacket packet = PrepareStorageOperation(ent, 2);
				packet.Write((byte)items.Count);
				foreach (Item item in items)
				{
					ItemIO.Send(item, packet, true);
				}
				packet.Send();
			}
		}

		public static void ReceiveStorageOperation(BinaryReader reader, int sender)
		{
			if (Main.netMode != 2)
			{
				return;
			}
			int ent = reader.ReadInt32();
			if (!TileEntity.ByID.ContainsKey(ent) || !(TileEntity.ByID[ent] is TEStorageHeart))
			{
				return;
			}
			TEStorageHeart heart = (TEStorageHeart)TileEntity.ByID[ent];
			byte op = reader.ReadByte();
			if (op == 0)
			{
				Item item = ItemIO.Receive(reader, true);
				heart.DepositItem(item);
				if (!item.IsAir)
				{
					ModPacket packet = PrepareOperationResult(op);
					ItemIO.Send(item, packet, true);
					packet.Send(sender);
				}
			}
			else if (op == 1 || op == 3)
			{
				Item item = ItemIO.Receive(reader, true);
				item = heart.TryWithdraw(item);
				if (!item.IsAir)
				{
					ModPacket packet = PrepareOperationResult(op);
					ItemIO.Send(item, packet, true);
					packet.Send(sender);
				}
			}
			else if (op == 2)
			{
				int count = reader.ReadByte();
				List<Item> items = new List<Item>();
				for (int k = 0; k < count; k++)
				{
					Item item = ItemIO.Receive(reader, true);
					heart.DepositItem(item);
					if (!item.IsAir)
					{
						items.Add(item);
					}
				}
				if (items.Count > 0)
				{
					ModPacket packet = PrepareOperationResult(op);
					packet.Write((byte)items.Count);
					foreach (Item item in items)
					{
						ItemIO.Send(item, packet, true);
					}
					packet.Send(sender);
				}
			}
			ModPacket packet2 = MagicStorage.Instance.GetPacket();
			packet2.Write((byte)MessageType.RefreshNetworkItems);
			packet2.Write(ent);
			packet2.Send();
		}

		public static void ReceiveOperationResult(BinaryReader reader)
		{
			if (Main.netMode != 1)
			{
				return;
			}
			Player player = Main.player[Main.myPlayer];
			byte op = reader.ReadByte();
			if (op == 0 || op == 1 || op == 3)
			{
				Item item = ItemIO.Receive(reader, true);
				if (op != 3 && Main.playerInventory && Main.mouseItem.IsAir)
				{
					Main.mouseItem = item;
					item = new Item();
				}
				else if (op != 3 && Main.playerInventory && Main.mouseItem.type == item.type)
				{
					int total = Main.mouseItem.stack + item.stack;
					if (total > Main.mouseItem.maxStack)
					{
						total = Main.mouseItem.maxStack;
					}
					int difference = total - Main.mouseItem.stack;
					Main.mouseItem.stack = total;
					item.stack -= total;
				}
				if (item.stack > 0)
				{
					item = player.GetItem(Main.myPlayer, item, false, true);
					if (!item.IsAir)
					{
						player.QuickSpawnClonedItem(item, item.stack);
					}
				}
			}
			else if (op == 2)
			{
				int count = reader.ReadByte();
				for (int k = 0; k < count; k++)
				{
					Item item = ItemIO.Receive(reader, true);
					item = player.GetItem(Main.myPlayer, item, false, true);
					if (!item.IsAir)
					{
						player.QuickSpawnClonedItem(item, item.stack);
					}
				}
			}
		}

		public static void ClientSendTEUpdate(int id)
		{
			if (Main.netMode == 1)
			{
				ModPacket packet = MagicStorage.Instance.GetPacket();
				packet.Write((byte)MessageType.ClientSendTEUpdate);
				packet.Write(id);
				TileEntity.Write(packet, TileEntity.ByID[id], true);
				packet.Send();
			}
		}

		public static void ReceiveClientSendTEUpdate(BinaryReader reader, int sender)
		{
			if (Main.netMode == 2)
			{
				int id = reader.ReadInt32();
				TileEntity ent = TileEntity.Read(reader, true);
				ent.ID = id;
				TileEntity.ByID[id] = ent;
				TileEntity.ByPosition[ent.Position] = ent;
				NetMessage.SendData(MessageID.TileEntitySharing, -1, sender, "", id, ent.Position.X, ent.Position.Y);
			}
		}
	}

	enum MessageType : byte
	{
		SearchAndRefreshNetwork,
		TryStorageOperation,
		StorageOperationResult,
		RefreshNetworkItems,
		ClientSendTEUpdate
	}
}