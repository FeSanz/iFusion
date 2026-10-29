using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using iFusion.Models;
using Newtonsoft.Json;

namespace iFusion.Services
{
    internal static class InstanceStore
    {
        private class Db
        {
            public string ActiveId { get; set; }
            public List<Instance> Instances { get; set; } = new List<Instance>();
        }

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iFusion", "instances.json");

        private static Db db = Read();

        public static List<Instance> All { get { return db.Instances; } }

        public static Instance Active
        {
            get { return db.Instances.FirstOrDefault(i => i.Id == db.ActiveId) ?? db.Instances.FirstOrDefault(); }
            set { db.ActiveId = value == null ? null : value.Id; Write(); }
        }

        public static void Save(Instance instance)
        {
            if (!db.Instances.Any(i => i.Id == instance.Id)) db.Instances.Add(instance);
            if (db.ActiveId == null) db.ActiveId = instance.Id;
            Write();
        }

        public static void Delete(Instance instance)
        {
            db.Instances.RemoveAll(i => i.Id == instance.Id);
            if (db.ActiveId == instance.Id) db.ActiveId = db.Instances.Select(i => i.Id).FirstOrDefault();
            Write();
        }

        private static Db Read()
        {
            try { return JsonConvert.DeserializeObject<Db>(File.ReadAllText(FilePath)) ?? new Db(); }
            catch (Exception) { return new Db(); }
        }

        private static void Write()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(db, Formatting.Indented));
        }
    }
}
