﻿using GameArchives.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GameArchives.FSGIMG
{
  public class FSGIMGPackage : AbstractPackage
  {
    public override string FileName { get; }

    public override IDirectory RootDirectory => root;

    private Stream filestream;
    private FSGIMGDirectory root;

    class file_descriptor
    {
      public uint filename_hash;
      public byte type;
      public uint offset;
      public long data_offset;
      public long size;
    }

    public FSGIMGPackage(string filename)
    {
      filestream = new FileStream(filename, FileMode.Open);
      if(filestream.ReadASCIINullTerminated() != "FSG-FILE-SYSTEM")
      {
        throw new InvalidDataException("FSG-FILE-SYSTEM header not found.");
      }
      filestream.ReadUInt32BE(); // unknown, == 2
      uint header_length = filestream.ReadUInt32BE();
      uint num_sectors = filestream.ReadUInt32BE();

      //points to a list of all (used) sectors?
      //starting at 0x180 and increasing by 0x80 up to (num_sectors + 3) << 17
      uint sectormap_offset = filestream.ReadUInt32BE(); 
      uint base_offset = filestream.ReadUInt32BE();
      filestream.ReadUInt32BE(); // unknown, read buffer size?
      filestream.ReadUInt32BE(); // unknown, == 8
      uint num_files = filestream.ReadUInt32BE();
      uint zero = filestream.ReadUInt32BE();
      uint checksum = filestream.ReadUInt32BE();
      var nodes = new Dictionary<uint,file_descriptor>((int)num_files);
      byte[] sector_types = new byte[num_sectors + (base_offset >> 17)];

      for (var i = 0; i < num_files; i++)
      {
        var node = new file_descriptor();
        node.filename_hash = filestream.ReadUInt32BE();
        node.type = (byte)filestream.ReadByte();
        node.offset = filestream.ReadUInt24BE();
        nodes.Add(node.filename_hash, node);
      }
      foreach(file_descriptor node in nodes.Values)
      {
        filestream.Position = node.offset;
        long offset = filestream.ReadUInt32BE();
        node.data_offset = (offset << 10) + base_offset;
        node.size = filestream.ReadUInt32BE();
      }
      root = RecursivelyGetFiles(null, ROOT_DIR, base_offset, "", nodes);
    }

    /// <summary>
    /// Parse a directory for its contents.
    /// </summary>
    /// <param name="name">The name of this directory.</param>
    /// <param name="base_offset">Location of its filename infos.</param>
    /// <param name="nodes">File descriptor dictionary</param>
    /// <returns></returns>
    private FSGIMGDirectory RecursivelyGetFiles(FSGIMGDirectory parent, string name, long base_offset, string path_acc, Dictionary<uint,file_descriptor> nodes)
    {
      filestream.Position = base_offset;
      string filename;
      FSGIMGDirectory ret = new FSGIMGDirectory(parent, name, base_offset);
      while ((filename = filestream.ReadASCIINullTerminated()) != "")
      {
        long pos = filestream.Position;
        string real_name = filename.Substring(1);
        file_descriptor desc;
        string nextPath = path_acc == "" ? real_name : $"{path_acc}/{real_name}";
        nodes.TryGetValue(Hash(nextPath), out desc);
        if (filename[0] == 'D')
        {
          ret.AddDir(RecursivelyGetFiles(ret, real_name, desc.data_offset, nextPath, nodes));
          filestream.Position = pos;
        }
        else if (filename[0] == 'F')
        {
          ret.AddFile(new FSGIMGFile(real_name, ret, filestream, (ulong)desc.data_offset, (ulong)desc.size));
        }
        else
        {
          throw new InvalidDataException($"Got invalid filename prefix: {filename[0]}.");
        }
      }
      return ret;
    }

    /// <summary>
    /// Hashes a path with a broken fnv132 hashing algorithm
    /// </summary>
    /// <param name="str"></param>
    /// <returns></returns>
    private uint Hash(string str)
    {
      if (str[0] == '/')
        str = str.Substring(1);
      str = str.ToUpper();
      uint hash = 2166136261U;
      for (var i = 0; i < str.Length; i++)
      {
        hash = (1677619U * hash) ^ (byte)str[i];
      }
      return hash;
    }

    public override void Dispose()
    {
      filestream.Close();
      filestream.Dispose();
    }
  }
}
