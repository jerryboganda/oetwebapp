import React from "react";
import { IconChevronsUp, IconChevronsDown } from "@tabler/icons-react";
import CustomDataTable from "@/Component/DataTable/CustomDataTable";
import { currenciesData } from "@/Data/Table/DataTable/buttonsDatatable";

type Currency = {
  code: string;
  name: string;
  totalAmount: string;
  availableAmount: string;
  availableBalance: string;
  percentageChange: number;
};

const ButtonsDatatable = () => {
  const columns = [
    { key: "code", header: "Currency Code" },
    { key: "name", header: "Currency" },
    { key: "totalAmount", header: "Price" },
    { key: "availableAmount", header: "High" },
    { key: "availableBalance", header: "Low" },
    {
      key: "change",
      header: "Change",
      render: (_: any, row: Currency) => (
        <div className="d-flex gap-1 align-items-center">
          {row.percentageChange >= 0 ? (
            <IconChevronsUp className="f-s-20 text-success" />
          ) : (
            <IconChevronsDown className="f-s-20 text-danger" />
          )}
          <h6
            className={`m-0 ${row.percentageChange >= 0 ? "text-success" : "text-danger"}`}
          >
            {row.percentageChange}%
          </h6>
        </div>
      ),
    },
  ];

  const dataWithKeys = currenciesData.map((item, index) => ({
    ...item,
    _key: `${item.code}-${index}`,
  }));

  return (
    <div className="app-scroll table-responsive app-datatable-default">
      <CustomDataTable
        title=""
        description=""
        columns={columns}
        data={dataWithKeys}
        rowKey="_key"
        showActions={false}
      />
    </div>
  );
};

export default ButtonsDatatable;
