﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Kember
{
    public static class KemberBackModule
    {

        /// <summary>
        /// Список расчитанных, но несохраненных метрик
        /// </summary>
#if DEBUG
        public static Dictionary<User,List<(string, DateTime, string)>> saves = new Dictionary<User, List<(string, DateTime, string)>>();
#else
        private static List<(string, DateTime, string)> saves = new List<(string, DateTime, string)>();
#endif

        /// <summary>
        /// Список содержащий реализации метрик
        /// </summary>
#if DEBUG
        public static List<IMetric> metrics = new List<IMetric>();
#else
        private static List<IMetric> metrics = new List<IMetric>();
#endif

        static KemberBackModule()
        {
            string path = new Uri(Assembly.GetAssembly(typeof(KemberBackModule)).Location).LocalPath;
            path = path.Substring(0, path.LastIndexOf("\\")) + @"\Module";
            try
            {
                string[] dlls = Directory.GetFiles(path, "*.dll");
                for (int i = 0; i < dlls.Length; i++)
                {
                    Type[] types = null;
                    Assembly assembly = Assembly.LoadFile(dlls[i]);
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch { types = new Type[0]; }
                    for (int j = 0; j < types.Length; j++)
                    {
                        if (!types[j].IsAbstract && !types[j].IsEnum && types[j].IsClass && types[j].GetInterface(typeof(IMetric).Name) != null)
                        {
                            metrics.Add(types[j].GetConstructor(new Type[0]).Invoke(new object[0]) as IMetric);
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Метод инициирубщий расчет метрики
        /// </summary>
        /// <param name="assembly">массив сборок для которых будет проводится расчет</param>
        /// <param name="args">аргументы для расчета метрики</param>
        /// <param name="metric">системное имя метрики</param>
        /// <returns></returns>
        public static (object, Assembly)[] Invoke(Assembly[] assembly, object args, string metric, User user)
        {
            (object, Assembly)[] results = new (object, Assembly)[assembly.Length];
            for (int i = 0; i < metrics.Count; i++)
            {
                if (metrics[i].GetType().Name == metric)
                {
                    for (int j = 0; j < results.Length; j++)
                    {
                        results[j] = (metrics[i].RunMetric(assembly[j], args), assembly[j]);
                    }
                    saves[user].Add((metric, DateTime.Now, metrics[i].Write(results)));
                    return results;
                }
            }
            return null;
        }

        /// <summary>
        /// Метод сохраняющий все расчитанные, но ексохраненные метрики
        /// </summary>
        /// <param name="key">Ключ пользователя для шифрования файлов сохранений</param>
        public static void Save(string key, User user)
        {
            if (!HashValidate(key, user)) throw new Exception();
            string name = user.Name;
            while (saves.Count > 0)
            {
                string path = "Data\\" + user.Name + saves[user][0].Item1 + saves[user][0].Item2 + ".kbr";
                AppDbContext.db.Logs.Add(new Log() { Owner = user, Metric = saves[user][0].Item1, TimeMark = saves[user][0].Item2, PathToFile = path });
                Encrypt(key, saves[user][0].Item3, path);
            }
        }

        /// <summary>
        /// Метод загружающий информацию из файлов сохранения в систему
        /// </summary>
        /// <param name="key">Ключ пользователя для расшифровки файлов сохранений</param>
        /// <param name="log">Информация о расчете который необходимо загрузить</param>
        /// <param name="assemblies">Возвращаемое значение - сборки, резултаты анализа, которых отражены в файле сохранения</param>
        /// <returns></returns>
        public static (object, Assembly)[] Load(string key, Log log, User user)
        {
            if (!HashValidate(key, user)) throw new Exception();
            foreach (var value in metrics)
            {
                if (value.GetType().Name == log.Metric)
                {
                    return value.Read(Decrypt(key, log.PathToFile));
                }
            }
            throw new Exception();
        }


        /// <summary>
        /// Метод валидации секретного ключа пользователя
        /// </summary>
        /// <param name="key">Ключ пользователя для шифрования/расшифровки файлов сохранений</param>
        /// <returns></returns>
#if DEBUG
        public static bool HashValidate(string key, User user)
#else
        private static bool HashValidate(string key)
#endif
        {
            if (user == null) throw new Exception();
            SHA512Managed sha = new SHA512Managed();
            string hash = Encoding.UTF8.GetString(sha.ComputeHash(Encoding.UTF8.GetBytes(key)));
            return hash == user.SecurityKey;
        }


        /// <summary>
        /// Метод шифрующий данные и записывающий их в файл
        /// </summary>
        /// <param name="skey">Ключ пользователя для шифрования файлов сохранений<</param>
        /// <param name="text">Текст, который подлежит шифрованию</param>
        /// <param name="path">Путь к файлу в который необходимо записать шифр</param>
#if DEBUG
        public static void Encrypt(string skey, string text, string path)
#else
        private static void Encrypt(string skey, string text, string path)
#endif
        {
            Aes aes = Aes.Create();
            byte[] key = Encoding.UTF8.GetBytes(skey);
            aes.Key = key;
            byte[] iv = aes.IV;
            FileStream file = new FileStream(path, FileMode.Create);
            file.Write(iv, 0, iv.Length);

            using (CryptoStream cryptoStream = new(file, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                using (StreamWriter encryptWriter = new(cryptoStream))
                {
                    encryptWriter.WriteLine(text);
                }
            }
        }


        /// <summary>
        /// Метод считвающий шифр из файла и расшифрующий эти данные
        /// </summary>
        /// <param name="skey">Ключ пользователя для расшифровки файлов сохранений</param>
        /// <param name="path">Путь к файлу из которого необходимо извлечь шифр</param>
        /// <returns></returns>
#if DEBUG
        public static string Decrypt(string skey, string path)
#else
        private static string Decrypt(string skey, string path)
#endif
        {
            FileStream fileStream = new(path, FileMode.Open);
            Aes aes = Aes.Create();
            byte[] iv = new byte[aes.IV.Length];
            int numBytesToRead = aes.IV.Length;
            int numBytesRead = 0;
            while (numBytesToRead > 0)
            {
                int n = fileStream.Read(iv, numBytesRead, numBytesToRead);
                if (n == 0) break;

                numBytesRead += n;
                numBytesToRead -= n;
            }
            byte[] key = Encoding.UTF8.GetBytes(skey);
            CryptoStream cryptoStream = new(fileStream, aes.CreateDecryptor(key, iv), CryptoStreamMode.Read);
            StreamReader decryptReader = new(cryptoStream);
            return decryptReader.ReadToEnd();
        }


        /// <summary>
        /// Метод авторизации пользователя
        /// </summary>
        /// <param name="name">Имя пользователя в системе</param>
        /// <returns></returns>
        public static User Login(string name)
        {
            User user = AppDbContext.db.Users.FirstOrDefault(t => t.Name == name);
            if(saves.Keys.Count != 0 && !saves.Keys.Contains(user))
            {
                saves.Add(user, new List<(string, DateTime, string)>());
            }
            return user;
        }

        /// <summary>
        /// Метод регистрации пользователя
        /// </summary>
        /// <param name="name">Имя пользователя в системе</param>
        /// <param name="key">Секретный ключ пользователя</param>
        public static User Registration(string name, string key)
        {
            SHA512Managed sha = new SHA512Managed();
            string hash = Encoding.UTF8.GetString(sha.ComputeHash(Encoding.UTF8.GetBytes(key)));
            User user = new User() { Name = name, SecurityKey = hash };
            AppDbContext.db.Users.Add(user);
            AppDbContext.db.SaveChanges();
            saves.Add(user, new List<(string, DateTime, string)>());
            return user;
        }
    }
}
