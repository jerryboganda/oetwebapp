import React from "react";
import { paymentData } from "@/Data/Table/DataTable/paymentData";
import CustomDataTable from "@/Component/DataTable/CustomDataTable";

const PaymentTable = () => {
  const columns = [
    { key: "name", header: "Name" },
    { key: "billNo", header: "Bill No" },
    { key: "tax", header: "Tax" },
    { key: "charges", header: "Charges" },
    {
      key: "discount",
      header: "Discount",
      render: (value: any) => (
        <span className="f-w-500 text-success">{value}</span>
      ),
    },
    { key: "billDate", header: "Bill Date" },
    { key: "total", header: "Total" },
  ];

  const dataWithIds = paymentData.map((item, index) => ({
    ...item,
    _id: `${item.billNo}-${index}`,
  }));

  return (
    <CustomDataTable
      title="Payment Details"
      description=""
      columns={columns}
      data={dataWithIds}
      rowKey="_id"
      showActions={false}
      showFooter
    />
  );
};

export default PaymentTable;
