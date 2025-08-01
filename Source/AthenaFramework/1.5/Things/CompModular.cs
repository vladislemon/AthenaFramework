﻿using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AthenaFramework
{
    [StaticConstructorOnStartup]
    public class CompModular : ThingComp, IEquippableGraphicGiver //, IFloatMenu
    {
        private CompProperties_Modular Props => props as CompProperties_Modular;

        public bool ejectableModules = true;
        public Command_Action ejectAction;
        public static readonly Texture2D cachedEjectTex = ContentFinder<Texture2D>.Get("UI/Gizmos/EjectModule");

        public ThingOwner<ThingWithComps> moduleHolder;
        public Dictionary<StatDef, float> gearStatOffsets = new Dictionary<StatDef, float>();
        public Dictionary<StatDef, float> statOffsets = new Dictionary<StatDef, float>();
        public Dictionary<StatDef, float> statFactors = new Dictionary<StatDef, float>();

        public virtual Pawn Holder
        {
            get
            {
                if (parent is Apparel)
                {
                    return (parent as Apparel).Wearer;
                }

                CompEquippable comp = parent.TryGetComp<CompEquippable>();

                if (comp != null)
                {
                    return comp.Holder;
                }

                return null;
            }
        }
        public virtual List<ModuleSlotPackage> GetAllSlots
        {
            get
            {
                return Props.slots;
            }
        }

        public virtual List<ModuleSlotPackage> GetOpenSlots(CompUseEffect_Module comp)
        {
            List<ModuleSlotPackage> validSlots = new List<ModuleSlotPackage>();

            for (int i = Props.slots.Count - 1; i >= 0; i--)
            {
                ModuleSlotPackage slot = Props.slots[i];

                if (!comp.Slots.Contains(slot.slotID))
                {
                    continue;
                }

                float spaceTaken = 0;
                bool invalid = false;

                for (int j = moduleHolder.Count - 1; j >= 0; j--)
                {
                    CompUseEffect_Module moduleComp = moduleHolder[j].TryGetComp<CompUseEffect_Module>();

                    if (moduleComp.usedSlot == slot.slotID)
                    {
                        spaceTaken += moduleComp.RequiredCapacity;

                        if (spaceTaken + comp.RequiredCapacity > slot.capacity && slot.capacity != -1)
                        {
                            invalid = true;
                            break;
                        }
                    }

                    if (moduleComp.ExcludeIDs.Intersect(comp.ExcludeIDs).Count() > 0)
                    {
                        invalid = true;
                        break;
                    }
                }

                if (!invalid)
                {
                    validSlots.Add(slot);
                }
            }

            return validSlots;
        }

        public virtual List<ApparelGraphicPackage> GetAdditionalGraphics
        {
            get
            {
                List<ApparelGraphicPackage> packages = new List<ApparelGraphicPackage>();

                for (int j = moduleHolder.Count - 1; j >= 0; j--)
                {
                    CompUseEffect_Module moduleComp = moduleHolder[j].TryGetComp<CompUseEffect_Module>();

                    if (moduleComp.GetGraphics != null)
                    {
                        packages = packages.Concat(moduleComp.GetGraphics).ToList();
                    }
                }

                return packages;
            }
        }

        public virtual void InstallModule(ThingWithComps thing)
        {
            Thing module = thing.SplitOff(1);
            CompUseEffect_Module comp = module.TryGetComp<CompUseEffect_Module>();

            if (!comp.Install(this))
            {
                GenPlace.TryPlaceThing(module, parent.Position, parent.Map, ThingPlaceMode.Near);
                return;
            }

            moduleHolder.TryAdd(module, false);
            RecacheGizmo();

            if (AthenaCache.renderCache.TryGetValue(parent.thingIDNumber, out List<IRenderable> mods))
            {
                for (int i = mods.Count - 1; i >= 0; i--)
                {
                    IRenderable renderable = mods[i];
                    renderable.RecacheGraphicData();
                }
            }
        }

        public virtual void RemoveModule(ThingWithComps thing)
        {
            CompUseEffect_Module comp = thing.TryGetComp<CompUseEffect_Module>();
            if (!comp.Remove(this))
            {
                return;
            }

            moduleHolder.TryDrop(thing, parent.Position, parent.Map, ThingPlaceMode.Near, 1, out Thing placedThing);
            RecacheGizmo();

            if (AthenaCache.renderCache.TryGetValue(parent.thingIDNumber, out List<IRenderable> mods))
            {
                for (int i = mods.Count - 1; i >= 0; i--)
                {
                    IRenderable renderable = mods[i];
                    renderable.RecacheGraphicData();
                }
            }
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            moduleHolder = new ThingOwner<ThingWithComps>();
            RecacheGizmo();

            // AthenaCache.AddCache(this, AthenaCache.menuCache, parent.thingIDNumber);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref moduleHolder, "moduleHolder");

            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
            {
                if (AthenaCache.renderCache.TryGetValue(parent.thingIDNumber, out List<IRenderable> mods))
                {
                    for (int i = mods.Count - 1; i >= 0; i--)
                    {
                        IRenderable renderable = mods[i];
                        renderable.RecacheGraphicData();
                    }
                }

                // AthenaCache.AddCache(this, AthenaCache.menuCache, parent.thingIDNumber);

                return;
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                return;
            }

            if (moduleHolder == null)
            {
                moduleHolder = new ThingOwner<ThingWithComps>();
                return;
            }

            for (int i = moduleHolder.Count - 1; i >= 0; i--)
            {
                ThingWithComps module = moduleHolder[i];
                CompUseEffect_Module comp = module.TryGetComp<CompUseEffect_Module>();

                comp.PostInit(this);
            }
        }

        public virtual void RecacheGizmo()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            ejectableModules = true;
            int validOptions = 0;

            for (int i = moduleHolder.Count - 1; i >= 0; i--)
            {
                ThingWithComps module = moduleHolder[i];
                CompUseEffect_Module comp = module.TryGetComp<CompUseEffect_Module>();

                if (!comp.Ejectable)
                {
                    options.Add(new FloatMenuOption("Unable to eject " + module.LabelCap + " (" + comp.GetSlot.slotName + ")", null, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0));
                    continue;
                }

                options.Add(new FloatMenuOption("Eject " + module.LabelCap + " (" + comp.GetSlot.slotName + ")", delegate () { RemoveModule(module); }));
                validOptions += 1;
            }

            if (options.Count == 0 || validOptions == 0)
            {
                options.Add(new FloatMenuOption("No ejectable modules", null, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0));
                ejectableModules = false;
            }

            ejectAction = new Command_Action();
            ejectAction.defaultLabel = "Eject module from " + parent.LabelCap;
            ejectAction.defaultDesc = "Eject a module from " + parent.LabelCap;
            ejectAction.icon = cachedEjectTex;
            ejectAction.action = delegate ()
            {
                Find.WindowStack.Add(new FloatMenu(options));
            };
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);

            // AthenaCache.RemoveCache(this, AthenaCache.menuCache, parent.thingIDNumber);

            if (!Props.dropModulesOnDestoy)
            {
                return;
            }

            foreach (var module in moduleHolder)
            {
                RemoveModule(module);
            }
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            if (ejectAction == null)
            {
                RecacheGizmo();
            }

            if (!ejectableModules)
            {
                yield break;
            }

            yield return ejectAction;
            yield break;
        }

        public IEnumerable<FloatMenuOption> ItemFloatMenuOptions(Pawn selPawn)
        {
            for (int i = selPawn.inventory.innerContainer.Count - 1; i >= 0; i--)
            {
                Thing item = selPawn.inventory.innerContainer[i];
                CompUsable_Module comp = item.TryGetComp<CompUsable_Module>();
                CompUseEffect_Module module = item.TryGetComp<CompUseEffect_Module>();

                if (comp == null || module == null)
                {
                    continue;
                }

                List<ModuleSlotPackage> slots = GetOpenSlots(module);

                for (int k = slots.Count - 1; k >= 0; k--)
                {
                    ModuleSlotPackage slot = slots[k];
                    Action action = delegate ()
                    {
                        if (selPawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Deadly, 1, -1, null, false))
                        {
                            //StartModuleJob(pawn, comp);
                        }
                    };

                    FloatMenuOption floatMenuOption = FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(comp.FloatMenuOptionLabel(selPawn) + " (" + slots[k].slotName + ")", action), selPawn, parent, "ReservedBy", null);
                    yield return floatMenuOption;
                }
            }

            yield break;
        }

        public IEnumerable<FloatMenuOption> PawnFloatMenuOptions(ThingWithComps thing) { yield break; }

    }

    public class CompProperties_Modular : CompProperties
    {
        public CompProperties_Modular()
        {
            this.compClass = typeof(CompModular);
        }

        // If all modules should be dropped upon the item getting destroyed
        public bool dropModulesOnDestoy = true;

        // List of open slots
        public List<ModuleSlotPackage> slots = new List<ModuleSlotPackage>();
    }
}
