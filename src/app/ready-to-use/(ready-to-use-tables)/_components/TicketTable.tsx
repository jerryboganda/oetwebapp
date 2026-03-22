import React from "react";
import { Card } from "reactstrap";
import { ticketsData } from "@/Data/Table/DataTable/ticketData";
import CustomDataTable from "@/Component/DataTable/CustomDataTable";

const TicketTable = () => {
  const columns = [
    { key: "agent", header: "AGENT" },
    { key: "department", header: "DEPARTMENT" },
    { key: "id", header: "ID", className: "f-w-500" },
    {
      key: "title",
      header: "TITLE",
      render: (value: string) => <p>{value}</p>,
    },
    {
      key: "activity",
      header: "ACTIVITY",
      render: (value: string) => (
        <span
          className={`text-${value === "No reply yet" ? "danger" : "success"} f-w-500`}
        >
          {value}
        </span>
      ),
    },
    { key: "date", header: "DATE" },
    {
      key: "priority",
      header: "PRIORITY",
      render: (value: string, item: any) => (
        <span className={`badge ${item.priorityClass}`}>{value}</span>
      ),
    },
  ];

  const dataWithIds = ticketsData.map((ticket, index) => ({
    ...ticket,
    _id: `${ticket.id}-${index}`,
  }));

  return (
    <Card>
      <CustomDataTable
        title="Ticket Detail"
        description=""
        columns={columns}
        data={dataWithIds}
        rowKey="_id"
        showActions={false}
      />
    </Card>
  );
};

export default TicketTable;
