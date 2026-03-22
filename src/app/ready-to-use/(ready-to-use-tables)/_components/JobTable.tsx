import React from "react";
import { Card, CardHeader } from "reactstrap";
import { paymentData } from "@/Data/Table/DataTable/jobData";
import { IconStar } from "@tabler/icons-react";
import CustomDataTable from "@/Component/DataTable/CustomDataTable";

interface PaymentData {
  avatar?: string;
  name: string;
  course: string;
  experience: string;
  rate: string;
  address: string;
  review: number;
  initials?: string;
}

const JobTable = () => {
  const columns = [
    {
      key: "avatar",
      header: "",
      render: (_: any, item: PaymentData) => (
        <div className="h-30 w-30 d-flex-center b-r-50 overflow-hidden text-bg-dark">
          {item.avatar && (
            <img
              src={item.avatar}
              alt={item.name}
              className="w-30 h-30 rounded-circle img-fluid"
            />
          )}
        </div>
      ),
    },
    { key: "name", header: "NAME" },
    { key: "course", header: "COURSE" },
    { key: "experience", header: "EXPERIENCE" },
    { key: "rate", header: "RATE" },
    { key: "address", header: "ADDRESS" },
    {
      key: "review",
      header: "REVIEW",
      render: () => (
        <div>
          {[...Array(5)].map((_, idx) => (
            <IconStar key={idx} size={16} color={idx ? "gold" : "lightgray"} />
          ))}
        </div>
      ),
    },
  ];

  return (
    <Card>
      <CardHeader>
        <h5>Job Resumes</h5>
      </CardHeader>

      <CustomDataTable
        columns={columns}
        data={paymentData}
        rowKey="name"
        title=""
        showTitle={false}
        showDescription={false}
        showActions={true}
        onEdit={(item) => console.log("Edit", item)}
        onDelete={(item) => console.log("Delete", item)}
      />
    </Card>
  );
};

export default JobTable;
