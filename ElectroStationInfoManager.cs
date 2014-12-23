using System;
using System.Linq;

using NodaTime;

using Sms.Readiness.Contracts.IUserInfo;
using Sms.Readiness.Contracts.IUserInfo.Classes;
using Sms.Readiness.Domain.Dictionaries;
using Sms.Readiness.Server.DAL;
using Sms.Readiness.Server.DAL.Providers;
using Sms.Readiness.Server.DispatchCenterDayStatusManager;
using Sms.Readiness.Server.ElectroStationLocksManager;

namespace VariousElectroStationInfo
{
    public static class ElectroStationInfoManager
    {
        /// <summary>
        /// Наименование электростанции.
        /// </summary>
        public static string GetName(
            string electroStationUniqueCode,
            DateTime operationDate,
            IDbContextProvider dbContextProvider)
        {
            ElectroStationVersion electroStation;
            using (var unitOfWork = new UnitOfWorkForRead(dbContextProvider))
            {
                electroStation = unitOfWork.ElectroStationVersion.FirstOrDefault(
                    electroStationUniqueCode,
                    operationDate);
            }

            if (electroStation == null)
            {
                throw new Exception();
            }

            return electroStation.Name;
        }

        /// <summary>
        /// Установлена ли блокировка на редактирование текущим пользователем.
        /// </summary>
        public static bool IsLockedByCurrentUserForEdit(
            string electroStationUniqueCode,
            DateTime operationDate,
            string currentUserLogin,
            ElectroStationLocksManager locksManager)
        {
            var electroStations = new[]
                {
                    new ElectroStationAndDataKey(
                        electroStationUniqueCode,
                        LocalDateTime.FromDateTime(operationDate).Date)
                };
            ElectroStationLock[] locks;
            using (locksManager.GetCriticalSection())
            {
                locks = locksManager.GetLocks(electroStations).ToArray();
            }

            return locks.Any() &&
                   locks.Single().UserLogin == currentUserLogin &&
                   locks.Single().EditLockTime != null;
        }

        /// <summary>
        /// Установлена ли блокировка на редактирование другим пользователем.
        /// </summary>
        public static bool IsLockedByAnotherUserForEdit(
            string electroStationUniqueCode,
            DateTime operationDate,
            string currentUserLogin,
            ElectroStationLocksManager locksManager)
        {
            var electroStations = new[]
                {
                    new ElectroStationAndDataKey(
                        electroStationUniqueCode,
                        LocalDateTime.FromDateTime(operationDate).Date)
                };
            ElectroStationLock[] locks;
            using (locksManager.GetCriticalSection())
            {
                locks = locksManager.GetLocks(electroStations).ToArray();
            }

            return locks.Any() &&
                   locks.Single().UserLogin != currentUserLogin &&
                   locks.Single().EditLockTime != null;
        }

        /// <summary>
        /// Актуализируется ДЦ пользователя.
        /// </summary>
        public static bool CouldBeUpdatedByCurrentUser(
            string electroStationUniqueCode,
            DateTime operationDate,
            string currentUserLogin,
            IUserInfoService userInfoService,
            IDbContextProvider dbContextProvider)
        {
            ElectroStationVersion electroStation;
            using (var unitOfWork = new UnitOfWorkForRead(dbContextProvider))
            {
                electroStation = unitOfWork.ElectroStationVersion.FirstOrDefault(
                    electroStationUniqueCode,
                    operationDate);
            }

            if (electroStation == null)
            {
                throw new Exception();
            }

            UserInfo userInfo;
            if (!userInfoService.TryGetUserInfo(currentUserLogin, out userInfo, operationDate))
            {
                throw new Exception();
            }

            return electroStation.ActualDispatchCenterUniqueCode == userInfo.DispatchCenter.UniqueCode;
        }

        /// <summary>
        /// Находится в ведении ДЦ пользователя.
        /// </summary>
        public static bool CouldBeConductedByUser(
            string electroStationUniqueCode,
            DateTime operationDate,
            string currentUserLogin,
            IUserInfoService userInfoService,
            IDbContextProvider dbContextProvider)
        {
            ElectroStationVersion electroStation;
            using (var unitOfWork = new UnitOfWorkForRead(dbContextProvider))
            {
                electroStation = unitOfWork.ElectroStationVersion.FirstOrDefault(
                    electroStationUniqueCode,
                    operationDate);
            }

            if (electroStation == null)
            {
                throw new Exception();
            }

            UserInfo userInfo;
            if (!userInfoService.TryGetUserInfo(currentUserLogin, out userInfo, operationDate))
            {
                throw new Exception();
            }

            return electroStation.ConductDispatchCenterUniqueCode == userInfo.DispatchCenter.UniqueCode;
        }

        /// <summary>
        /// Расчетная блокировка станции на дату.
        /// </summary>
        /// <returns></returns>
        public static bool IsOperationLockSet(
            string electroStationUniqueCode,
            DateTime operationDate,
            ElectroStationLocksManager locksManager)
        {
            var electroStations = new[]
                {
                    new ElectroStationAndDataKey(
                        electroStationUniqueCode,
                        LocalDateTime.FromDateTime(operationDate).Date)
                };
            ElectroStationLock[] locks;
            using (locksManager.GetCriticalSection())
            {
                locks = locksManager.GetLocks(electroStations).ToArray();
            }

            return locks.Any() &&
                   locks.Single().OperationLockTime != null;
        }

        /// <summary>
        /// Есть ли блокировка отчетных суток.
        /// </summary>
        public static bool IsDayLockSetForParentDc(
            string electroStationUniqueCode,
            DateTime operationDate,
            IDbContextProvider dbContextProvider,
            IDispatchCenterDayStatusManager dayStatusManager)
        {
            ElectroStationVersion electroStation;
            using (var unitOfWork = new UnitOfWorkForRead(dbContextProvider))
            {
                electroStation = unitOfWork.ElectroStationVersion.FirstOrDefault(
                    electroStationUniqueCode,
                    operationDate);
            }

            if (electroStation == null)
            {
                throw new Exception();
            }

            DispatchCenterTotalDayStatus actualDcLockState =
                !string.IsNullOrWhiteSpace(electroStation.ActualDispatchCenterUniqueCode)
                    ? dayStatusManager.GetStatus(
                        electroStation.ActualDispatchCenterUniqueCode,
                        operationDate)
                    : null;
            DispatchCenterTotalDayStatus conductDcLockState =
                !string.IsNullOrWhiteSpace(electroStation.ConductDispatchCenterUniqueCode)
                    ? dayStatusManager.GetStatus(
                        electroStation.ConductDispatchCenterUniqueCode,
                        operationDate)
                    : null;

            return (actualDcLockState != null && (actualDcLockState.IsLocked || actualDcLockState.IsParentLocked)) ||
                   (conductDcLockState != null && (conductDcLockState.IsLocked || conductDcLockState.IsParentLocked));
        }

        /// <summary>
        /// Загружены ли исходные данные для станции по системам на данные сутки (список систем).
        /// </summary>
        public static bool IsInitialDataLoaded()
        {
            throw new NotImplementedException();
        }
    }
}