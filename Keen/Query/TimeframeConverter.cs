﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;


namespace Keen.Core.Query
{
    /// <summary>
    /// Used for converting IQueryTimeframe instances to/from JSON.
    /// </summary>
    internal class TimeframeConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(IQueryTimeframe).IsAssignableFrom(objectType);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            // QueryAbsoluteTimeframe has fields with JsonProperty attributes, so shouldn't need
            // custom converter support for writing JSON.

            if (value is QueryRelativeTimeframe)
            {
                writer.WriteValue(value.ToString());
            }
        }

        public override object ReadJson(JsonReader reader,
                                        Type objectType,
                                        object existingValue,
                                        JsonSerializer serializer)
        {
            var jsonToken = JToken.Load(reader);

            // If it's just a string, then it's a relative timeframe, otherwise parse as an
            // absolute timeframe.
            if (JTokenType.String == jsonToken.Type)
            {
                return QueryRelativeTimeframe.Create(jsonToken.ToString(Formatting.None));
            }
            else
            {
                return jsonToken.ToObject<QueryAbsoluteTimeframe>();
            }
        }
    }
}