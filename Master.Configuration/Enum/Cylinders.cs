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
        TC_UC_Ent_Stopper_Cyl,
        TC_UC_Scan_Stopper_Cyl,
        TC_UC_Ld_Stopper_Cyl,
        TC_Int_Stopper1_Cyl,
        TC_Int_Stopper2_Cyl,
        TC_UC_Ld_Lifter_Cyl,
        TC_UC_Unld_Lifter_Cyl,
        BFC_Stopper1_Cyl,
        BFC_Stopper2_Cyl,
        UC_Ld_Lifter_Hold_Cyl,
        UC_Unld_Lifter_Hold_Cyl,
        UC_Ld_Gripper_Cyl,
        UC_Unld_Gripper_Cyl,
        Glue_Weigh_Cover_Cyl,
        NG_Stopper_Cyl,
        Stn1_Wiping_Cyl1,
        Stn1_Wiping_Cyl2,
        Stn1_Maint_Door_Cyl,
        Stn1_Disp_Lock_Cyl,
        Stn1_Glue_Disp_Cyl,
        Stn2_Wiping_Cyl1,
        Stn2_Wiping_Cyl2,
        Stn2_Maint_Door_Cyl,
        Stn2_Disp_Lock_Cyl,
        Stn2_Glue_Disp_Cyl
    }
    #endregion
}
