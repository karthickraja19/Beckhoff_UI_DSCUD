using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Configuration
{
    #region List of all cylinders
    /// <summary>List of all cylinders. TE010 cylinders, from the IO list (valve kits 1-4 and their reed switches); same order as Data\Cylinders.json.</summary>
    public enum Cylinders
    {
        Start=0,
        BFC_Stopper_1,
        BFC_Stopper_2,
        TC_UC_Entry_Stopper,
        TC_UC_Scanning_Stopper,
        TC_UC_Loading_Stopper,
        TC_Intermediate_Stopper_1,
        TC_Intermediate_Stopper_2,
        TC_UC_Loading_Lifter,
        TC_UC_Unloading_Lifter,
        UC_Loading_Lifter_Holding_Cylinder,
        UC_Unloading_Lifter_Holding_Cylinder,
        UC_Loading_Gripper_Cylinder,
        UC_Unloading_Gripper_Cylinder,
        Stn1_Wiping_Module_Cylinder_1,
        Stn1_Wiping_Module_Cylinder_2,
        Stn1_Maintenance_Door_Cylinder,
        Stn1_Glue_Dispenser_Cylinder,
        Stn1_Dispenser_Locking_Cylinder,
        Stn2_Wiping_Module_Cylinder_1,
        Stn2_Wiping_Module_Cylinder_2,
        Stn2_Maintenance_Door_Cylinder,
        Stn2_Glue_Dispenser_Cylinder,
        Stn2_Dispenser_Locking_Cylinder,
        NG_Stopper_Cylinder

        //TC_UC_Ent_Stopper_Cyl,
        //TC_UC_Scan_Stopper_Cyl,
        //TC_UC_Ld_Stopper_Cyl,
        //TC_Int_Stopper1_Cyl,
        //TC_Int_Stopper2_Cyl,
        //TC_UC_Ld_Lifter_Cyl,
        //TC_UC_Unld_Lifter_Cyl,
        //BFC_Stopper1_Cyl,
        //BFC_Stopper2_Cyl,
        //UC_Ld_Lifter_Hold_Cyl,
        //UC_Unld_Lifter_Hold_Cyl,
        //UC_Ld_Gripper_Cyl,
        //UC_Unld_Gripper_Cyl,
        //Glue_Weigh_Cover_Cyl,
        //NG_Stopper_Cyl,
        //Stn1_Wiping_Cyl1,
        //Stn1_Wiping_Cyl2,
        //Stn1_Maint_Door_Cyl,
        //Stn1_Disp_Lock_Cyl,
        //Stn1_Glue_Disp_Cyl,
        //Stn2_Wiping_Cyl1,
        //Stn2_Wiping_Cyl2,
        //Stn2_Maint_Door_Cyl,
        //Stn2_Disp_Lock_Cyl,
        //Stn2_Glue_Disp_Cyl
    }
    #endregion
}
