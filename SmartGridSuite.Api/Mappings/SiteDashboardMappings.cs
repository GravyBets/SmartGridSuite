using SmartGridSuite.Api.Services.ParentSync.Models;
using SmartGridSuite.Contracts.SiteDashboard;
using System.Linq;

namespace SmartGridSuite.Api.Mappings
{
    public static class SiteDashboardMappings
    {
        //To determine if site is MR, IG, DACs, RX
        public static SiteDashboardRouteInfoDto ToDto(this SiteDashboardRouteInfo source)
        {
            return new SiteDashboardRouteInfoDto
            {
                SiteId = source.SiteId,
                SiteTypeId = source.SiteTypeId,
                SiteType = source.SiteType,
                ConfigId = source.ConfigId,
                SiteConfigName = source.SiteConfigName,
                PrimaryCommType = source.PrimaryCommType,
                SecondaryCommType = source.SecondaryCommType,
                SiteConfigDescription = source.SiteConfigDescription
            };
        }

        public static SiteDashboardResponseDto ToDto(this SiteDashboardResponse source)
        {
            return new SiteDashboardResponseDto
            {
                SiteId = source.SiteId,
                DashboardKind = source.DashboardKind,
                Route = source.Route.ToDto(),
                Data = MapSiteDashboardData(source.DashboardKind, source.Data)
            };
        }

        //Site History
        public static SiteHistoryPreviewDto ToDto(this SiteHistoryPreviewRow source)
        {
            return new SiteHistoryPreviewDto
            {
                HistoryId = source.HistoryId,
                SiteId = source.SiteId,
                VisitDate = source.VisitDate,
                PrimaryTech = source.PrimaryTech,
                SecondaryTech = source.SecondaryTech,
                Narrative = source.Narrative
            };
        }

        //DACs
        public static DacsSiteDashboardDto ToDto(this DacsSiteDashboardRow source)
        {
            return new DacsSiteDashboardDto
            {
                SiteId = source.SiteId,
                SiteStatus = source.SiteStatus,
                SiteType = source.SiteType,
                SiteConfigName = source.SiteConfigName,
                PrimaryCommType = source.PrimaryCommType,
                SecondaryCommType = source.SecondaryCommType,
                SiteConfigDescription = source.SiteConfigDescription,

                PrimaryCommsIp = source.PrimaryCommsIp,
                TunnelIp = source.TunnelIp,
                RtuIp = source.RtuIp,

                TopName = source.TopName,
                TopDescription = source.TopDescription,
                TopSector = source.TopSector,

                StreetNo = source.StreetNo,
                StreetName = source.StreetName,
                City = source.City,
                County = source.County,
                StateCode = source.StateCode,
                ZipCode = source.ZipCode,

                Latitude = source.Latitude,
                Longitude = source.Longitude,

                History = source.History.Select(x => x.ToDto()).ToList()
            };
        }

        //Range Extenders
        public static RxSiteDashboardDto ToDto(this RxSiteDashboardRow source)
        {
            return new RxSiteDashboardDto
            {
                SiteId = source.SiteId,
                SiteStatus = source.SiteStatus,
                SiteType = source.SiteType,
                SiteConfigName = source.SiteConfigName,
                SiteConfigDescription = source.SiteConfigDescription,

                MeterNumber = source.MeterNumber,
                MacAddress = source.MacAddress,
                PolePoint = source.PolePoint,
                TransformerGln = source.TransformerGln,

                StreetNo = source.StreetNo,
                StreetName = source.StreetName,
                City = source.City,
                County = source.County,
                StateCode = source.StateCode,
                ZipCode = source.ZipCode,

                Latitude = source.Latitude,
                Longitude = source.Longitude,

                History = source.History.Select(x => x.ToDto()).ToList()
            };
        }

        //AMS Sites
        public static AmsSiteDashboardDto ToDto(this AmsSiteDashboardRow source)
        {
            return new AmsSiteDashboardDto
            {
                SiteId = source.SiteId,
                SiteStatus = source.SiteStatus,

                SiteType = source.SiteType,
                SiteConfigName = source.SiteConfigName,
                PrimaryCommType = source.PrimaryCommType,
                SecondaryCommType = source.SecondaryCommType,
                SiteConfigDescription = source.SiteConfigDescription,

                PrimaryCommsIdentifier = source.PrimaryCommsIdentifier,
                PrimaryCommsIp = source.PrimaryCommsIp,

                SecondaryLanIp = source.SecondaryLanIp,
                SecondaryWanIp = source.SecondaryWanIp,

                SecondaryCommsIdentifier = source.SecondaryCommsIdentifier,
                SecondaryCommsUsername = source.SecondaryCommsUsername,
                SecondaryCommsSsid = source.SecondaryCommsSsid,
                SecondaryCommsPassword = source.SecondaryCommsPassword,
                SecondarySimNumber = source.SecondarySimNumber,

                AntennaSerialNumber = source.AntennaSerialNumber,
                EnclosureSerialNumber = source.EnclosureSerialNumber,
                EnclosureModel = source.EnclosureModel,

                TopName = source.TopName,
                TopDescription = source.TopDescription,
                TopSector = source.TopSector,

                StreetNo = source.StreetNo,
                StreetName = source.StreetName,
                City = source.City,
                County = source.County,
                StateCode = source.StateCode,
                ZipCode = source.ZipCode,

                Latitude = source.Latitude,
                Longitude = source.Longitude,

                History = source.History.Select(x => x.ToDto()).ToList()
            };
        }

        //IG Sites
        public static IgsdSiteDashboardDto ToDto(this IgsdSiteDashboardRow source)
        {
            return new IgsdSiteDashboardDto
            {
                SiteId = source.SiteId,

                SiteStatus = source.SiteStatus,
                SiteType = source.SiteType,

                SiteConfigName = source.SiteConfigName,
                PrimaryCommType = source.PrimaryCommType,
                SecondaryCommType = source.SecondaryCommType,
                SiteConfigDescription = source.SiteConfigDescription,

                PrimaryCommsIdentifier = source.PrimaryCommsIdentifier,
                PrimaryCommsIp = source.PrimaryCommsIp,
                PrimaryLanIp = source.PrimaryLanIp,
                PrimaryWanIp = source.PrimaryWanIp,
                PrimaryTunnelIp = source.PrimaryTunnelIp,
                PrimaryRtuIp = source.PrimaryRtuIp,

                SecondaryCommsIdentifier = source.SecondaryCommsIdentifier,
                SecondaryWanIp = source.SecondaryWanIp,
                SecondaryLanIp = source.SecondaryLanIp,
                SecondaryTunnelIp = source.SecondaryTunnelIp,
                SecondaryRtuIp = source.SecondaryRtuIp,

                AntennaSerialNumber = source.AntennaSerialNumber,
                EnclosureSerialNumber = source.EnclosureSerialNumber,
                EnclosureModel = source.EnclosureModel,
                CyberlockSerialNumber = source.CyberlockSerialNumber,
                TunnelPsk = source.TunnelPsk,

                TopName = source.TopName,
                TopDescription = source.TopDescription,
                TopSector = source.TopSector,

                StreetNo = source.StreetNo,
                StreetName = source.StreetName,
                City = source.City,
                County = source.County,
                StateCode = source.StateCode,
                ZipCode = source.ZipCode,

                Latitude = source.Latitude,
                Longitude = source.Longitude,

                History = source.History.Select(x => x.ToDto()).ToList()
            };
        }

        //Helpers
        private static object? MapSiteDashboardData(string dashboardKind, object? data)
        {
            if (data is null)
            {
                return null;
            }

            return dashboardKind switch
            {
                SiteDashboardKinds.AmsMr when data is AmsSiteDashboardRow row => row.ToDto(),
                SiteDashboardKinds.Dacs when data is DacsSiteDashboardRow row => row.ToDto(),
                SiteDashboardKinds.Rx when data is RxSiteDashboardRow row => row.ToDto(),
                SiteDashboardKinds.Igsd when data is IgsdSiteDashboardRow row => row.ToDto(),
                _ => null
            };
        }
    }
}