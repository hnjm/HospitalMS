﻿using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System.Configuration;
using Oracle.DataAccess.Client;
using System.Collections.Generic;
using HospitalMS.HelperClasses;
using System.Runtime.InteropServices;

namespace HospitalMS
{
    public partial class BillForm : Form
    {
        // ========================== Rounding Edges ==========================//
        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn
        (
           int nLeftRect,     // x-coordinate of upper-left corner
           int nTopRect,      // y-coordinate of upper-left corner
           int nRightRect,    // x-coordinate of lower-right corner
           int nBottomRect,   // y-coordinate of lower-right corner
           int nWidthEllipse, // width of ellipse
           int nHeightEllipse // height of ellipse
        );
        // =====================================================================//

        private string dbConnection;
        private OracleConnection conn;
        private List<int> medicineCodeList;
        private List<int> medicineQuantityList;
        private float totalMedsCost;
        private float totalBillCost;
        private string fullName;

        public BillForm()
        {
            InitializeComponent();
        }

        private void BillForm_Load(object sender, EventArgs e)
        {
            panelContainer.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, panelContainer.Width, panelContainer.Height, 30, 30));
            dbConnection = ConfigurationManager.ConnectionStrings["databaseConnection"].ConnectionString;
            conn = new OracleConnection(dbConnection);
            conn.Open();

            // Fill The ComboBox with The ReportIDs From Case_Report Table
            getReportIDsIntoCmbBox();
            // List for temporary storing MedicineCode for all meds
            medicineCodeList = new List<int>();
            // List for temporary storing Medicine Quantity Taken for all meds
            medicineQuantityList = new List<int>();
            // Total Meds Price of taken medicines --> Medicine Price * Quantity Taken
            totalMedsCost = 0;
            // Total Bill Cost the patient has to pay --> totalMedsPrice + Days Stayed * dayAccomodationCost
            totalBillCost = 0;
            // Full Name of the selected Patient
            fullName = "";
        }

        // ================== RETRIEVE FROM DATABASE ================= //
        private void getReportIDsIntoCmbBox()
        {
            OracleCommand cmd = new OracleCommand();
            cmd.Connection = conn;
            cmd.CommandText = "SELECT reportid FROM case_report";
            cmd.CommandType = CommandType.Text;
            OracleDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
                comboReportID.Items.Add(reader[0].ToString());
            reader.Close();
        }

        private int getMaxID()
        {
            OracleCommand cmd = new OracleCommand();
            cmd.Connection = conn;
            cmd.CommandText = "SELECT MAX(BillNumber) FROM case_bill";
            cmd.CommandType = CommandType.Text;
            int maxID;
            try
            {
                maxID = Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch
            {
                maxID = 112;
            }
            return maxID;
        }

        private void comboPatientNationalID_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboReportID.SelectedIndex != -1)
            {
                btnAddBill.Enabled = true;
                totalMedsCost = 0;
                totalBillCost = 0;
                int reportID = Convert.ToInt32(comboReportID.SelectedItem);
                setPatientFullName(reportID);
                setMedicineLists(reportID);
                calculateTotalMedsCost();
                int daysStayed = getDaysStayed(reportID);
                calculateTotalBillCost(daysStayed);

                txtPatientFullname.Text = fullName;
                txtTotalMedsCost.Text = totalMedsCost.ToString();
                txtTotalBillCost.Text = totalBillCost.ToString();

                medicineCodeList.Clear();
                medicineQuantityList.Clear();
            }
        }

        private void setPatientFullName(int reportID)
        {
            OracleCommand cmd = new OracleCommand();
            cmd.Connection = conn;
            cmd.CommandText = "SELECT FirstName, LastName FROM Patient WHERE NationalID = (SELECT PatientID FROM case_report WHERE reportid = :reportID)";
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.Add("reportID", reportID);
            OracleDataReader reader = cmd.ExecuteReader();
            reader.Read();
            string firstName = reader[0].ToString();
            string lastName = reader[1].ToString();
            fullName = firstName + " " + lastName;
        }

        private void setMedicineLists(int reportID)
        {
            OracleCommand cmd = new OracleCommand();
            cmd.Connection = conn;
            cmd.CommandText = "SELECT MedicineCode, MedicQuantityTaken FROM report_and_medicine WHERE reportid = :reportID";
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.Add("reportID", reportID);
            OracleDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                medicineCodeList.Add(Convert.ToInt32(reader[0].ToString()));
                medicineQuantityList.Add(Convert.ToInt32(reader[1].ToString()));
            }
            reader.Close();
        }

        private float getMedicinePrice(int codeNumber)
        {
            OracleCommand cmd = new OracleCommand();
            cmd.Connection = conn;
            cmd.CommandText = "SELECT price FROM medicine WHERE codenumber = :codeNumber";
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.Add("codeNumber", codeNumber);
            float price = (float)Convert.ToDouble(cmd.ExecuteScalar());
            return price;
        }

        private void calculateTotalMedsCost()
        {
            for (int i = 0; i < medicineCodeList.Count; ++i)
            {
                float price = getMedicinePrice(medicineCodeList[i]);
                totalMedsCost += (price * medicineQuantityList[i]);
            }
        }

        private int getDaysStayed(int reportID)
        {
            OracleCommand cmd = new OracleCommand();
            cmd.Connection = conn;
            cmd.CommandText = "SELECT dateout - datein FROM case_report WHERE reportid = :reportID";
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.Add("reportID", reportID);
            int daysStayed = Convert.ToInt32(cmd.ExecuteScalar());
            return daysStayed;
        }

        private void calculateTotalBillCost(int daysStayed)
        {
            totalBillCost = totalMedsCost + daysStayed * GlobalData.dayAccomodationCost;
        }

        // ================== STORE TO DATABASE ================= //

        private void btnAddBill_Click(object sender, EventArgs e)
        {
            int billID = getMaxID() + 1;
            int reportID = Convert.ToInt32(comboReportID.Text);

            if (isValidBill(reportID))
                insertBill(billID, reportID);
            else
                MessageBox.Show("The Bill is Already Added !!", "Patient Bill", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

        private bool isValidBill(int reportID)
        {
            OracleCommand cmd = new OracleCommand();
            cmd.Connection = conn;
            cmd.CommandText = "SELECT COUNT(BillNumber) FROM case_bill WHERE reportid = :reportID";
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.Add("reportID", reportID);
            int res = Convert.ToInt32(cmd.ExecuteScalar());
            if (res != 0) return false; // the bill exists in the database
            return true;
        }

        private void insertBill(int billID, int reportID)
        {
            OracleCommand cmd = new OracleCommand();
            cmd.Connection = conn;
            cmd.CommandText = "Insert into case_bill values (:billID, :reportID, :medsCost, :billCost)";
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.Add("billID", billID);
            cmd.Parameters.Add("reportID", reportID);
            cmd.Parameters.Add("medsCost", totalMedsCost);
            cmd.Parameters.Add("billCost", totalBillCost);
            int res = cmd.ExecuteNonQuery();
            if (res != -1)
                MessageBox.Show("Bill For Patient " + fullName + " has been Added Successfully !", "Patient Bill", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show("Something went Wrong ...", "Patient Bill", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // ======================================================= //
        private void BillForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            conn.Dispose();
        }

    }
}
