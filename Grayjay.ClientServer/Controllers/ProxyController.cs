using Grayjay.ClientServer.Parsers;
using Grayjay.ClientServer.Proxy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using System;
using System.Diagnostics;
using System.Net;
using System.Web;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class ProxyController: ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> Image(string url)
        {
            if (string.IsNullOrEmpty(url))
                throw new BadHttpRequestException("Missing url");

            using (HttpClient client = new HttpClient())
            {
                var result = await client.GetAsync(url);
                if (result.StatusCode != HttpStatusCode.OK)
                    return StatusCode((int)result.StatusCode);

                //if (!result.Content.Headers.ContentType.MediaType.StartsWith("image/"))
                //    return BadRequest("Only allowed images");

                foreach (var header in result.Content.Headers)
                    Response.Headers.Add(header.Key, new StringValues(header.Value.ToArray()));

                return new FileStreamResult(result.Content.ReadAsStream(), result.Content.Headers.ContentType.MediaType);
            }
        }


        [HttpGet]
        public async Task<IActionResult> HLS(string url, bool proxyMedia)
        {
            var playlist = await GenerateProxiedHLS(url, proxyMedia, $"{Request.Scheme}://{Request.Host.Value}");

            return new ContentResult()
            {
                Content = playlist.GenerateM3U8(),
                ContentType = "application/x-mpegurl"
            };
        }

        public static async Task<Parsers.HLS.IHLSPlaylist> GenerateProxiedHLS(string hlsUrl, bool proxyMedia, string baseUri)
        {
            if (string.IsNullOrEmpty(hlsUrl))
                throw new BadHttpRequestException("Missing url");

            using (HttpClient client = new HttpClient())
            {
                var result = await client.GetAsync(hlsUrl);
                if (result.StatusCode != HttpStatusCode.OK)
                    throw new InvalidDataException($"Failed to fetch manifest [" + result.StatusCode + "]");

                var body = await result.Content.ReadAsStringAsync();

                try
                {
                    var masterPlaylist = Parsers.HLS.ParseMasterPlaylist(body, result.RequestMessage.RequestUri.ToString());
                    if (masterPlaylist.UnHandled.Any(x=>x.StartsWith("#EXTINF:")))
                        throw new ArgumentException("Is a variant playlist");
                    masterPlaylist = ProxyHLSMasterPlaylist(baseUri, masterPlaylist, proxyMedia);
                    return masterPlaylist;
                }
                catch
                {
                    var playlist = Parsers.HLS.ParseVariantPlaylist(body, hlsUrl);
                    playlist = ProxyHLSPlaylist(baseUri, playlist, proxyMedia);
                    return playlist;
                }
            }
        }



        public static HLS.MasterPlaylist ProxyHLSMasterPlaylist(string baseUri, HLS.MasterPlaylist hlsMasterPlaylist, bool proxyMedia)
        {
            foreach (var vp in hlsMasterPlaylist.MediaRenditions)
                vp.Uri = $"{baseUri}/proxy/HLS?url={HttpUtility.UrlEncode(vp.Uri)}&proxyMedia={proxyMedia}";
            foreach (var vp in hlsMasterPlaylist.VariantPlaylistsRefs)
                vp.Url = $"{baseUri}/proxy/HLS?url={HttpUtility.UrlEncode(vp.Url)}&proxyMedia={proxyMedia}";

            return hlsMasterPlaylist;
        }

        public static HLS.VariantPlaylist ProxyHLSPlaylist(string baseUri, HLS.VariantPlaylist hlsMediaPlaylist, bool proxyMedia)
        {
            if (!proxyMedia)
                return hlsMediaPlaylist;

            foreach (var s in hlsMediaPlaylist.Segments)
            {
                if (!(s is HLS.MediaSegment ms))
                    continue;

                var uri = new Uri(baseUri);
                var isLoopback = uri.Host.Contains("127.0.0.1");
                var ip = IPAddress.Parse(uri.Host);
                ms.Uri = HttpProxy.Get(isLoopback).Add(new HttpProxyRegistryEntry()
                {
                    Url = ms.Uri,
                    FollowRedirects = true,
                    RequestHeaderOptions = new RequestHeaderOptions()
                    {
                        HeadersToInject = new Dictionary<string, string>()
                        {
                            { "Origin", null }
                        }
                    },
                    ResponseHeaderOptions = new ResponseHeaderOptions()
                    {
                        InjectPermissiveCORS = true
                    }
                }, ip);
            }

            if(!string.IsNullOrEmpty(hlsMediaPlaylist.MapUrl))
                hlsMediaPlaylist.MapUrl = HttpProxy.Get().Add(new HttpProxyRegistryEntry()
                {
                    Url = hlsMediaPlaylist.MapUrl,
                    FollowRedirects = true,
                    RequestHeaderOptions = new RequestHeaderOptions()
                    {
                        HeadersToInject = new Dictionary<string, string>()
                        {
                            { "Origin", null }
                        }
                    },
                    ResponseHeaderOptions = new ResponseHeaderOptions()
                    {
                        InjectPermissiveCORS = true
                    }
                });


            return hlsMediaPlaylist;
        }



        /*
        [HttpGet]
        public async Task<IActionResult> Proxy(string url)
        {
            if (string.IsNullOrEmpty(url))
                throw new BadHttpRequestException("Missing url");

            using (HttpClient client = new HttpClient())
            {
                foreach (var header in Request.Headers)
                    client.DefaultRequestHeaders.Add(header.Key, header.Value.ToList());

                var result = await client.GetAsync(url);
                if (result.StatusCode != HttpStatusCode.OK)
                    return StatusCode((int)result.StatusCode);

                foreach (var header in result.Headers)
                    Response.Headers.Add(header.Key, new StringValues(header.Value.ToArray()));

                return new FileStreamResult(result.Content.ReadAsStream(), result.Content.Headers.ContentType.MediaType);
            }
        }*/
    }
}
