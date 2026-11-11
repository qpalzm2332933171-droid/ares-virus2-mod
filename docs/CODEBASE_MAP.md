# 代码库结构图（自动统计）

源：`src_real/Assembly-CSharp`，类声明 3450 个。

## 类名前缀分布（Top 25）

| 前缀 | 类数 |
|---|---:|
| `CAI` | 1100 |
| `CLan` | 467 |
| `s* (struct/config)` | 259 |
| `CFSM` | 249 |
| `CUI_` | 209 |
| `UI*` | 190 |
| `CBehavior` | 143 |
| `CPlace` | 59 |
| `CMain` | 48 |
| `C` | 36 |
| `P` | 33 |
| `T` | 22 |
| `CCondition` | 20 |
| `CScene` | 20 |
| `S` | 18 |
| `CCollider` | 17 |
| `CBuilding` | 16 |
| `CMarket` | 15 |
| `CEffect` | 14 |
| `CTask` | 13 |
| `CHome` | 12 |
| `CBoat` | 11 |
| `CDialog` | 11 |
| `CGame` | 11 |
| `CSuit` | 10 |

## 子系统（实现 I*System 的类）

| 类 | 基类/接口 | 文件 |
|---|---|---|
| `CAchievementCupSystem` | `IAchievementCupSystem` | `CAchievementCupSystem.cs` |
| `CAndroidPaySystem` | `IEventExecuteSink, INetworkHandler, IAndroidPaySystem` | `CAndroidPaySystem.cs` |
| `CAnniversaryCheckInSystem` | `IAnniversaryCheckInSystem` | `CAnniversaryCheckInSystem.cs` |
| `CAntiCheatSystem` | `INetworkHandler, IAntiCheatSystem` | `CAntiCheatSystem.cs` |
| `CAvatarSystem` | `IAvatarSystem` | `CAvatarSystem.cs` |
| `CBoxingSystem` | `IBoxingSystem` | `CBoxingSystem.cs` |
| `CBuffSystem` | `IBuffSystem` | `CBuffSystem.cs` |
| `CBuildingSystem` | `IBuildingSystem` | `CBuildingSystem.cs` |
| `CChallengeSystem` | `IChallengeSystem` | `CChallengeSystem.cs` |
| `CCharacterSystem` | `ICharacterSystem` | `CCharacterSystem.cs` |
| `CClockSystem` | `IClockSystem, ITimerHandler` | `CClockSystem.cs` |
| `CContainerSystem` | `IContainerSystem` | `CContainerSystem.cs` |
| `CDLCSystem` | `IDLCSystem` | `CDLCSystem.cs` |
| `CDailySystem` | `IDailySystem, IEventExecuteSink` | `CDailySystem.cs` |
| `CEatCountSystem` | `IEatCountSystem, IEventExecuteSink` | `CEatCountSystem.cs` |
| `CEntitySystem` | `IEntitySystem` | `CEntitySystem.cs` |
| `CEquipmentSystem` | `IEquipmentSystem` | `CEquipmentSystem.cs` |
| `CExchangeSystem` | `IExchangeSystem, IEventExecuteSink` | `CExchangeSystem.cs` |
| `CFSMSystem` | `IFSMSystem` | `CFSMSystem.cs` |
| `CFSMTriggerSystem` | `IFSMTriggerSystem` | `CFSMTriggerSystem.cs` |
| `CFavorabilityEventSystem` | `IFavorabilityEventSystem` | `CFavorabilityEventSystem.cs` |
| `CFavorabilitySystem` | `IFavorabilitySystem` | `CFavorabilitySystem.cs` |
| `CFightObjSystem` | `IFightObjSystem, IEventExecuteSink` | `CFightObjSystem.cs` |
| `CFightSystem` | `IFightSystem` | `CFightSystem.cs` |
| `CFightUISystem` | `IFightUISystem, IFrameHandler` | `CFightUISystem.cs` |
| `CFishingCountSystem` | `IFishingCountSystem, IEventExecuteSink` | `CFishingCountSystem.cs` |
| `CGameSystem` | `IGameSystem, IDBServerHandler` | `CGameSystem.cs` |
| `CHireSystem` | `IHireSystem` | `CHireSystem.cs` |
| `CHomeItemSystem` | `IHomeItemSystem` | `CHomeItemSystem.cs` |
| `CHuntSystem` | `IHuntSystem` | `CHuntSystem.cs` |
| `CHunterSystem` | `IHunterSystem, IEventExecuteSink, IClockChangeHandler` | `CHunterSystem.cs` |
| `CIndicatorSystem` | `IIndicatorSystem` | `CIndicatorSystem.cs` |
| `CInformationSystem` | `IInformationSystem, IEventExecuteSink` | `CInformationSystem.cs` |
| `CKillCountSystem` | `IKillCountSystem, IEventExecuteSink` | `CKillCountSystem.cs` |
| `CLanSaveSystem` | `ILanSaveSystem, IEventExecuteSink` | `CLanSaveSystem.cs` |
| `CMaterialSystem` | `IMaterialSystem` | `CMaterialSystem.cs` |
| `CMiniGameScoreSystem` | `IMiniGameScoreSystem` | `CMiniGameScoreSystem.cs` |
| `CNewGuideSystem` | `INewGuideSystem, IEventExecuteSink` | `CNewGuideSystem.cs` |
| `CNotifySystem` | `INotifySystem` | `CNotifySystem.cs` |
| `CPaySystem` | `IPaySystem` | `CPaySystem.cs` |
| `CPropertySystem` | `IPropertySystem` | `CPropertySystem.cs` |
| `CPurchaseRestrictionSystem` | `IPurchaseRestrictionSystem` | `CPurchaseRestrictionSystem.cs` |
| `CRefitSystem` | `IRefitSystem` | `CRefitSystem.cs` |
| `CRentSystem` | `IRentSystem` | `CRentSystem.cs` |
| `CResearchSystem` | `IResearchSystem` | `CResearchSystem.cs` |
| `CSaleSystem` | `ISaleSystem` | `CSaleSystem.cs` |
| `CSceneSystem` | `ISceneSystem, IEventExecuteSink` | `CSceneSystem.cs` |
| `CSkillSystem` | `ISkillSystem` | `CSkillSystem.cs` |
| `CTalentSystem` | `ITalentSystem` | `CTalentSystem.cs` |
| `CTalkSystem` | `ITalkSystem` | `CTalkSystem.cs` |
| `CTaskSystem` | `ITaskSystem` | `CTaskSystem.cs` |
| `CTipsSystem` | `ITipsSystem` | `CTipsSystem.cs` |
| `CUserLogSystem` | `IUserLogSystem` | `CUserLogSystem.cs` |
| `CWeaponCountSystem` | `IWeaponCountSystem, IEventExecuteSink` | `CWeaponCountSystem.cs` |
| `CWorkerSystem` | `IWorkerSystem, IEventExecuteSink` | `CWorkerSystem.cs` |
| `CZhouNianGameScoreSystem` | `IZhouNianGameScoreSystem` | `CZhouNianGameScoreSystem.cs` |
| `CiOSPaySystem` | `IEventExecuteSink, INetworkHandler, IiOSPaySystem` | `CiOSPaySystem.cs` |

## 配置加载器

| 类 | 文件 |
|---|---|
| `CAccessoryDetailLoader` | `CAccessoryDetailLoader.cs` |
| `CActionConfigLoader` | `CActionConfigLoader.cs` |
| `CActivityRecycleConfigLoader` | `CActivityRecycleConfigLoader.cs` |
| `CAnimalConfigLoader` | `CAnimalConfigLoader.cs` |
| `CAnniversaryCardConfigLoader` | `CAnniversaryCardConfigLoader.cs` |
| `CAnniversaryCheckInConfigLoader` | `CAnniversaryCheckInConfigLoader.cs` |
| `CAntiCheatConfigLoader` | `CAntiCheatConfigLoader.cs` |
| `CArmyConfigLoader` | `CArmyConfigLoader.cs` |
| `CAvatarConfigLoader` | `CAvatarConfigLoader.cs` |
| `CAvatarResourceConfigLoader` | `CAvatarResourceConfigLoader.cs` |
| `CBaitConfigLoader` | `CBaitConfigLoader.cs` |
| `CBinaryFormatLoader` | `CBinaryFormatLoader.cs` |
| `CBlackList_ImportantItemConfigLoader` | `CBlackList_ImportantItemConfigLoader.cs` |
| `CBlackList_OtherItemConfigLoader` | `CBlackList_OtherItemConfigLoader.cs` |
| `CBlackList_PayItemConfigLoader` | `CBlackList_PayItemConfigLoader.cs` |
| `CBlackList_PointItemConfigLoader` | `CBlackList_PointItemConfigLoader.cs` |
| `CBlackList_RaffleItemConfigLoader` | `CBlackList_RaffleItemConfigLoader.cs` |
| `CBoxingConfigLoader` | `CBoxingConfigLoader.cs` |
| `CBuffConfigLoader` | `CBuffConfigLoader.cs` |
| `CBuildingConfigLoader` | `CBuildingConfigLoader.cs` |
| `CBuildingSaleConfigLoader` | `CBuildingSaleConfigLoader.cs` |
| `CBuildingUpgradeConfigLoader` | `CBuildingUpgradeConfigLoader.cs` |
| `CCSVFileLoader` | `CCSVFileLoader.cs` |
| `CCampConfigLoader` | `CCampConfigLoader.cs` |
| `CChallegeCountConfigLoader` | `CChallegeCountConfigLoader.cs` |
| `CCharacterConfigLoader` | `CCharacterConfigLoader.cs` |
| `CConditionConfigLoader` | `CConditionConfigLoader.cs` |
| `CCountMainConfigLoader` | `CCountMainConfigLoader.cs` |
| `CDefaultWeaponConfigLoader` | `CDefaultWeaponConfigLoader.cs` |
| `CDismantleConfigLoader` | `CDismantleConfigLoader.cs` |
| `CDoubletwinConfigLoader` | `CDoubletwinConfigLoader.cs` |
| `CDropConfigLoader` | `CDropConfigLoader.cs` |
| `CDropRandomConfigLoader` | `CDropRandomConfigLoader.cs` |
| `CEatCountConfigLoader` | `CEatCountConfigLoader.cs` |
| `CEffectConfigLoader` | `CEffectConfigLoader.cs` |
| `CEquipCountConfigLoader` | `CEquipCountConfigLoader.cs` |
| `CEquipmentConfigLoader` | `CEquipmentConfigLoader.cs` |
| `CExchangeConfigLoader` | `CExchangeConfigLoader.cs` |
| `CFarmlandConfigLoader` | `CFarmlandConfigLoader.cs` |
| `CFavorabilityConfigLoader` | `CFavorabilityConfigLoader.cs` |
| `CFavorabilityEventConfigLoader` | `CFavorabilityEventConfigLoader.cs` |
| `CFavorabilityLevelConfigLoader` | `CFavorabilityLevelConfigLoader.cs` |
| `CFertilizerConfigLoader` | `CFertilizerConfigLoader.cs` |
| `CFileLoader` | `CFileLoader.cs` |
| `CFishingCountConfigLoader` | `CFishingCountConfigLoader.cs` |
| `CFootballMatchConfigLoader` | `CFootballMatchConfigLoader.cs` |
| `CFurnaceConfigLoader` | `CFurnaceConfigLoader.cs` |
| `CFurnitureConfigLoader` | `CFurnitureConfigLoader.cs` |
| `CHireConfigLoader` | `CHireConfigLoader.cs` |
| `CHistoryNoticeConfigLoader` | `CHistoryNoticeConfigLoader.cs` |
| `CHolidayExchangeConfigLoader` | `CHolidayExchangeConfigLoader.cs` |
| `CHolidayImportantExchangeConfigLoader` | `CHolidayImportantExchangeConfigLoader.cs` |
| `CHomeItemConfigLoader` | `CHomeItemConfigLoader.cs` |
| `CHunterConfigLoader` | `CHunterConfigLoader.cs` |
| `CInformationConfigLoader` | `CInformationConfigLoader.cs` |
| `CInformationTalkConfigLoader` | `CInformationTalkConfigLoader.cs` |
| `CLanConfigLoader` | `CLanConfigLoader.cs` |
| `CLetterConfigLoader` | `CLetterConfigLoader.cs` |
| `CMakeConfigLoader` | `CMakeConfigLoader.cs` |
| `CMakeOverrideConfigLoader` | `CMakeOverrideConfigLoader.cs` |
| `CMarket_BackGiftConfigLoader` | `CMarket_BackGiftConfigLoader.cs` |
| `CMarket_CardConfigLoader` | `CMarket_CardConfigLoader.cs` |
| `CMarket_ExchangeDepotConfigLoader` | `CMarket_ExchangeDepotConfigLoader.cs` |
| `CMarket_ExchangeTraderConfigLoader` | `CMarket_ExchangeTraderConfigLoader.cs` |
| `CMarket_GardenCoinShopConfigLoader` | `CMarket_GardenCoinShopConfigLoader.cs` |
| `CMarket_GardenPointShopConfigLoader` | `CMarket_GardenPointShopConfigLoader.cs` |
| `CMarket_GiftConfigLoader` | `CMarket_GiftConfigLoader.cs` |
| `CMarket_PassportConfigLoader` | `CMarket_PassportConfigLoader.cs` |
| `CMarket_PassportRewardConfigLoader` | `CMarket_PassportRewardConfigLoader.cs` |
| `CMarket_PlayConfigLoader` | `CMarket_PlayConfigLoader.cs` |
| `CMarket_PlayPointConfigLoader` | `CMarket_PlayPointConfigLoader.cs` |
| `CMarket_RafflePoolConfigLoader` | `CMarket_RafflePoolConfigLoader.cs` |
| `CMarket_RaffleTraderConfigLoader` | `CMarket_RaffleTraderConfigLoader.cs` |
| `CMarket_SignInConfigLoader` | `CMarket_SignInConfigLoader.cs` |
| `CMarket_WeekGiftConfigLoader` | `CMarket_WeekGiftConfigLoader.cs` |
| `CMaterialConfigLoader` | `CMaterialConfigLoader.cs` |
| `CMaterialMakeConfigLoader` | `CMaterialMakeConfigLoader.cs` |
| `CMaterialMakeOverrideConfigLoader` | `CMaterialMakeOverrideConfigLoader.cs` |
| `CMonsterChallangeConfigLoader` | `CMonsterChallangeConfigLoader.cs` |
| `CMonsterDefenceConfigLoader` | `CMonsterDefenceConfigLoader.cs` |
| `CMonsterResearchConfigLoader` | `CMonsterResearchConfigLoader.cs` |
| `CMountConfigLoader` | `CMountConfigLoader.cs` |
| `CPayDLCConfigLoader` | `CPayDLCConfigLoader.cs` |
| `CPayDiamondConfigLoader` | `CPayDiamondConfigLoader.cs` |
| `CPayLimitGiftConfigLoader` | `CPayLimitGiftConfigLoader.cs` |
| `CPayShopConfigLoader` | `CPayShopConfigLoader.cs` |
| `CPersonLibraryConfigLoader` | `CPersonLibraryConfigLoader.cs` |
| `CPreViewDetailConfigLoader` | `CPreViewDetailConfigLoader.cs` |
| `CProduceConfigLoader` | `CProduceConfigLoader.cs` |
| `CRecoveryConfigLoader` | `CRecoveryConfigLoader.cs` |
| `CRefitConfigLoader` | `CRefitConfigLoader.cs` |
| `CRentConfigLoader` | `CRentConfigLoader.cs` |
| `CReplyConfigLoader` | `CReplyConfigLoader.cs` |
| `CSaleConfigLoader` | `CSaleConfigLoader.cs` |
| `CSceneConfigLoader` | `CSceneConfigLoader.cs` |
| `CSceneEventConfigLoader` | `CSceneEventConfigLoader.cs` |
| `CSceneManualConfigLoader` | `CSceneManualConfigLoader.cs` |
| `CSkillConfigLoader` | `CSkillConfigLoader.cs` |
| `CSuitEffectConfigLoader` | `CSuitEffectConfigLoader.cs` |
| `CTalentConfigLoader` | `CTalentConfigLoader.cs` |
| `CTalentLimitConfigLoader` | `CTalentLimitConfigLoader.cs` |
| `CTalkConfigLoader` | `CTalkConfigLoader.cs` |
| `CTaskConfigLoader` | `CTaskConfigLoader.cs` |
| `CTaskTriggerConfigLoader` | `CTaskTriggerConfigLoader.cs` |
| `CTextLoader` | `CTextLoader.cs` |
| `CToolConfigLoader` | `CToolConfigLoader.cs` |
| `CTowerBuildingConfigLoader` | `CTowerBuildingConfigLoader.cs` |
| `CUnitConfigLoader` | `CUnitConfigLoader.cs` |
| `CWorkerConfigLoader` | `CWorkerConfigLoader.cs` |
| `CWorkerUpgradeConfigLoader` | `CWorkerUpgradeConfigLoader.cs` |
| `CXMLFileLoader` | `CXMLFileLoader.cs` |
| `CZhouNianGameConfigLoader` | `CZhouNianGameConfigLoader.cs` |
| `CZhouNianGamePointConfigLoader` | `CZhouNianGamePointConfigLoader.cs` |