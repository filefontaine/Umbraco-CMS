﻿using K4os.Compression.LZ4;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Umbraco.Web.PublishedCache.NuCache.DataSource
{
    /// <summary>
    /// Serializes/Deserializes <see cref="ContentNestedData"/> document to the SQL Database as bytes using MessagePack
    /// </summary>
    internal class MsgPackContentNestedDataSerializer : IContentNestedDataByteSerializer
    {
        private MessagePackSerializerOptions _options;
        private readonly NuCachePropertyCompressionOptions _propertyOptions;

        public MsgPackContentNestedDataSerializer(INuCachePropertyOptionsFactory propertyOptionsFactory = null)
        {
            var defaultOptions = ContractlessStandardResolver.Options;

            var resolver = CompositeResolver.Create(

                // TODO: We want to be able to intern the strings for aliases when deserializing like we do for Newtonsoft but I'm unsure exactly how
                // to do that but it would seem to be with a custom message pack resolver but I haven't quite figured out based on the docs how
                // to do that since that is part of the int key -> string mapping operation, might have to see the source code to figure that one out.

                // resolver custom types first
                // new ContentNestedDataResolver(),

                // finally use standard resolver
                defaultOptions.Resolver
            );

            _options = defaultOptions
                .WithResolver(resolver)
                .WithCompression(MessagePackCompression.Lz4BlockArray);
            _propertyOptions = propertyOptionsFactory?.GetNuCachePropertyOptions() ?? NuCachePropertyCompressionOptions.Empty;
        }

        public string ToJson(string serialized)
        {
            var bin = Convert.FromBase64String(serialized);
            var json = MessagePackSerializer.ConvertToJson(bin, _options);
            return json;
        }

        public ContentNestedData Deserialize(string data)
        {
            var bin = Convert.FromBase64String(data);
            var nestedData = MessagePackSerializer.Deserialize<ContentNestedData>(bin, _options);
            Expand(nestedData);
            return nestedData;
        }

        public string Serialize(ContentNestedData nestedData)
        {
            Compress(nestedData);
            var bin = MessagePackSerializer.Serialize(nestedData, _options);
            return Convert.ToBase64String(bin);
        }

        public ContentNestedData DeserializeBytes(byte[] data)
        {
            var nestedData = MessagePackSerializer.Deserialize<ContentNestedData>(data, _options);
            Expand(nestedData);
            return nestedData;
        }

        public byte[] SerializeBytes(ContentNestedData nestedData)
        {
            Compress(nestedData);
            return MessagePackSerializer.Serialize(nestedData, _options);
        }

        /// <summary>
        /// Used during serialization to compress properties and map property names to shorter names
        /// </summary>
        /// <param name="nestedData"></param>
        private void Compress(ContentNestedData nestedData)
        {
            if (_propertyOptions.PropertyMap != null && _propertyOptions.PropertyMap.Count > 0)
            {
                foreach (var map in _propertyOptions.PropertyMap)
                {
                    if (map.Value.CompressLevel.Equals(NucachePropertyCompressionLevel.SQLDatabase))
                    {
                        if (nestedData.PropertyData.TryGetValue(map.Key, out PropertyData[] properties))
                        {
                            foreach (var property in properties.Where(x => x.Value != null && x.Value is string))
                            {
                                property.Value = LZ4Pickler.Pickle(Encoding.UTF8.GetBytes(property.Value as string), _propertyOptions.LZ4CompressionLevel);
                            }
                        }
                    }

                    // if there is an alias map for this property then use that instead of the real property alias
                    // (used to save memory, the mapped alias is normally a single char or at least a smaller string)
                    if (map.Value.MappedAlias != null && !map.Key.Equals(map.Value.MappedAlias)
                        && nestedData.PropertyData.TryGetValue(map.Key, out PropertyData[] properties2))
                    {
                        nestedData.PropertyData.Remove(map.Key);
                        nestedData.PropertyData.Add(map.Value.MappedAlias, properties2);
                    }
                }
            }
        }

        /// <summary>
        /// Used during deserialization to map the property data as lazy or expand the value and re-map back to the true property aliases
        /// </summary>
        /// <param name="nestedData"></param>
        private void Expand(ContentNestedData nestedData)
        {
            if (_propertyOptions.PropertyMap != null && _propertyOptions.PropertyMap.Count > 0)
            {
                foreach (var map in _propertyOptions.PropertyMap)
                {
                    if (map.Value.CompressLevel.Equals(NucachePropertyCompressionLevel.SQLDatabase))
                    {
                        // if there is an alias map for this property then re-map to the real property alias                    
                        if (map.Value.MappedAlias != null && !map.Key.Equals(map.Value.MappedAlias)
                            && nestedData.PropertyData.TryGetValue(map.Value.MappedAlias, out PropertyData[] properties2))
                        {
                            nestedData.PropertyData.Remove(map.Value.MappedAlias);
                            nestedData.PropertyData.Add(map.Key, properties2);
                        }

                        if (nestedData.PropertyData.TryGetValue(map.Key, out PropertyData[] properties))
                        {
                            foreach (var pdata in properties)
                            {
                                if (!(pdata.Value is null) && pdata.Value is byte[] byteArrayValue)
                                {
                                    //Compressed string
                                    switch (map.Value.DecompressLevel)
                                    {
                                        case NucachePropertyDecompressionLevel.Lazy:
                                            pdata.Value = new LazyCompressedString(byteArrayValue);
                                            break;
                                        case NucachePropertyDecompressionLevel.NotCompressed:
                                            //Shouldn't be any not compressed
                                            throw new InvalidOperationException($"{NucachePropertyDecompressionLevel.NotCompressed} cannot be a decompression option for property {map.Key} since it's compresion option is {map.Value.CompressLevel}");
                                        case NucachePropertyDecompressionLevel.Immediate:
                                        default:
                                            pdata.Value = Encoding.UTF8.GetString(LZ4Pickler.Unpickle(byteArrayValue));
                                            break;
                                    }
                                }
                            }
                        }
                    }

                   
                }
            }
        }

        //private class ContentNestedDataResolver : IFormatterResolver
        //{
        //    // GetFormatter<T>'s get cost should be minimized so use type cache.
        //    public IMessagePackFormatter<T> GetFormatter<T>() => FormatterCache<T>.Formatter;

        //    private static class FormatterCache<T>
        //    {
        //        public static readonly IMessagePackFormatter<T> Formatter;

        //        // generic's static constructor should be minimized for reduce type generation size!
        //        // use outer helper method.
        //        static FormatterCache()
        //        {
        //            Formatter = (IMessagePackFormatter<T>)SampleCustomResolverGetFormatterHelper.GetFormatter(typeof(T));
        //        }
        //    }
        //}

        //internal static class SampleCustomResolverGetFormatterHelper
        //{
        //    // If type is concrete type, use type-formatter map
        //    static readonly Dictionary<Type, object> _formatterMap = new Dictionary<Type, object>()
        //    {
        //        {typeof(ContentNestedData), new ContentNestedDataFormatter()}
        //        // add more your own custom serializers.
        //    };

        //    internal static object GetFormatter(Type t)
        //    {
        //        object formatter;
        //        if (_formatterMap.TryGetValue(t, out formatter))
        //        {
        //            return formatter;
        //        }

        //        // If target type is generics, use MakeGenericType.
        //        if (t.IsGenericParameter && t.GetGenericTypeDefinition() == typeof(ValueTuple<,>))
        //        {
        //            return Activator.CreateInstance(typeof(ValueTupleFormatter<,>).MakeGenericType(t.GenericTypeArguments));
        //        }

        //        // If type can not get, must return null for fallback mechanism.
        //        return null;
        //    }
        //}

        //public class ContentNestedDataFormatter : IMessagePackFormatter<ContentNestedData>
        //{
        //    public void Serialize(ref MessagePackWriter writer, ContentNestedData value, MessagePackSerializerOptions options)
        //    {
        //        if (value == null)
        //        {
        //            writer.WriteNil();
        //            return;
        //        }

        //        writer.WriteArrayHeader(3);
        //        writer.WriteString(value.UrlSegment);
        //        writer.WriteString(value.FullName);
        //        writer.WriteString(value.Age);

        //        writer.WriteString(value.FullName);
        //    }

        //    public ContentNestedData Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        //    {
        //        if (reader.TryReadNil())
        //        {
        //            return null;
        //        }

        //        options.Security.DepthStep(ref reader);

        //        var path = reader.ReadString();

        //        reader.Depth--;
        //        return new FileInfo(path);
        //    }
        //}
    }
}
