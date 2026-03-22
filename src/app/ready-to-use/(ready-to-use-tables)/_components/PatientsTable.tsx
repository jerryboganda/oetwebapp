import { Card, CardHeader } from "reactstrap";
import { patientsData } from "@/Data/Table/DataTable/patientsData";
import CustomDataTable from "@/Component/DataTable/CustomDataTable";

interface Patient {
  id: number;
  name: string;
  avatar?: string;
  initials: string;
  address: string;
  patientId: string;
  contact: string;
  age: number;
  lastVisit: string;
  status: string;
  statusColor: string;
}

const PatientsTable = () => {
  const columns = [
    {
      key: "name",
      header: "Name",
      render: (_: any, patient: Patient) => (
        <div className="d-flex align-items-center">
          {patient.avatar ? (
            <div className="h-30 w-30 d-flex-center b-r-50 overflow-hidden text-bg-dark">
              <img
                src={patient.avatar}
                alt={patient.name}
                className="img-fluid w-30 h-30 rounded-circle"
                style={{ width: 30, height: 30, borderRadius: "50%" }}
              />
            </div>
          ) : (
            <span
              className={`bg-${patient.statusColor} h-30 w-30 d-flex-center b-r-50 text-white`}
            >
              {patient.initials}
            </span>
          )}
          <p className="mb-0 ps-2">{patient.name}</p>
        </div>
      ),
    },
    { key: "address", header: "Address" },
    { key: "patientId", header: "Patient ID" },
    { key: "contact", header: "Number" },
    { key: "age", header: "Age" },
    { key: "lastVisit", header: "Last Visit" },
    {
      key: "status",
      header: "Status",
      render: (_: any, patient: Patient) => (
        <span className={`badge text-outline-${patient.statusColor}`}>
          {patient.status}
        </span>
      ),
    },
  ];

  return (
    <Card>
      <CardHeader>
        <h5>Patients List</h5>
      </CardHeader>

      <CustomDataTable
        columns={columns}
        data={patientsData}
        rowKey="id"
        title=""
        showTitle={false}
        showDescription={false}
        showActions={false}
      />
    </Card>
  );
};

export default PatientsTable;
