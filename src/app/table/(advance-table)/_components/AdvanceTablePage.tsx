"use client";
import React, { useEffect, useState } from "react";
import {
  Card,
  CardBody,
  CardHeader,
  Col,
  Container,
  Modal,
  ModalHeader,
  ModalBody,
  ModalFooter,
  Row,
  Table,
  Button,
  Input,
  Label,
  FormGroup,
  Form,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { projectData } from "@/Data/Table/DataTable/advancedDatatable";
import { IconArrowsMove, IconEdit, IconTrash } from "@tabler/icons-react";
import { Table2Columns } from "iconoir-react";
import CustomDataTable from "@/Component/DataTable/CustomDataTable";

type Project = {
  id: number;
  title: string;
  creator: string;
  status: "New" | "Inprogress" | "Completed" | string;
  manager: string;
  startDate: string;
  endDate: string;
  logo: string;
  date?: string;
};

type Employee = {
  name: string;
  position: string;
  status: "High" | "Medium" | "Lower" | string;
  email: string;
  id: string;
  salary: string;
  date: string;
};

const AdvanceTablePage = () => {
  const [employees, setEmployees] = useState<Employee[]>([
    {
      name: "Alison Mused",
      position: "Accountant",
      status: "Medium",
      email: "alison@gmail.com",
      id: "#167",
      salary: "$2000",
      date: "December 1, 2024",
    },
    {
      name: "Amelia Commishun",
      position: "Junior Technical Author",
      status: "High",
      email: "amelia@gmail.com",
      id: "#289",
      salary: "$1200",
      date: "December 1, 2024",
    },
    {
      name: "Molly Story",
      position: "Software Engineer",
      status: "Medium",
      email: "molly@gmail.com",
      id: "#138",
      salary: "$4500",
      date: "December 1, 2024",
    },
    {
      name: "Diana Book",
      position: "Integration Specialist",
      status: "Lower",
      email: "diana@gmail.com",
      id: "#280",
      salary: "$5000",
      date: "December 1, 2024",
    },
    {
      name: "Evan Scope",
      position: "Sales Assistant",
      status: "High",
      email: "evan@gmail.com",
      id: "#165",
      salary: "$2560",
      date: "December 1, 2024",
    },
    {
      name: "Julia Sik",
      position: "Accountant",
      status: "Medium",
      email: "julia@gmail.com",
      id: "#245",
      salary: "$2400",
      date: "December 1, 2024",
    },
    {
      name: "Kylie Down",
      position: "Chief Operating Officer",
      status: "Lower",
      email: "kylie@gmail.com",
      id: "#167",
      salary: "$1800",
      date: "December 1, 2024",
    },
  ]);

  const [isModalOpen, setIsModalOpen] = useState(false);
  const [selectedProject, setSelectedProject] = useState<Project | null>(null);
  const [draggedIndex, setDraggedIndex] = useState<number | null>(null);

  const [selectedRowIds, setSelectedRowIds] = useState<number[]>([]);
  const [selectAll, setSelectAll] = useState(false);

  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [deleteCandidate, setDeleteCandidate] = useState<Project | null>(null);
  const [projects, setProjects] = useState<Project[]>(projectData);

  const toggleModal = () => setIsModalOpen(!isModalOpen);

  const handleEditClick = (item: Project) => {
    setSelectedProject(item);
    setIsModalOpen(true);
  };

  const handleDeleteClick = (item: Project) => {
    setDeleteCandidate(item);
    setShowDeleteModal(true);
  };

  const confirmDelete = () => {
    if (deleteCandidate) {
      setProjects((prev) => prev.filter((p) => p.id !== deleteCandidate.id));
      setShowDeleteModal(false);
    }
  };

  const handleDragStart = (
    e: React.DragEvent<HTMLTableRowElement>,
    index: number
  ) => {
    e.dataTransfer.effectAllowed = "move";
    e.dataTransfer.setData("text/plain", index.toString());
    setDraggedIndex(index);
  };

  const handleDragOver = (e: React.DragEvent<HTMLTableRowElement>) => {
    e.preventDefault();
    e.dataTransfer.dropEffect = "move";
  };

  const handleDrop = (
    e: React.DragEvent<HTMLTableRowElement>,
    targetIndex: number
  ) => {
    e.preventDefault();
    if (draggedIndex !== null && draggedIndex !== targetIndex) {
      const newEmployees = [...employees];
      const [draggedEmployee] = newEmployees.splice(draggedIndex, 1);
      newEmployees.splice(targetIndex, 0, draggedEmployee as Employee);
      setEmployees(newEmployees);
      setDraggedIndex(null);
    }
  };

  const handleDragEnd = () => {
    setDraggedIndex(null);
  };

  useEffect(() => {
    setSelectAll(selectedRowIds.length === projects.length);
  }, [selectedRowIds, projects]);

  const projectColumns = [
    {
      key: "select",
      header: (
        <input
          type="checkbox"
          checked={selectAll}
          onChange={(e) => {
            const checked = e.target.checked;
            setSelectAll(checked);
            setSelectedRowIds(checked ? projects.map((p) => p.id) : []);
          }}
        />
      ),
      render: (_: any, item: Project) => (
        <input
          type="checkbox"
          checked={selectedRowIds.includes(item.id)}
          onChange={(e) => {
            const updated = e.target.checked
              ? [...selectedRowIds, item.id]
              : selectedRowIds.filter((id) => id !== item.id);
            setSelectedRowIds(updated);
            setSelectAll(updated.length === projects.length);
          }}
        />
      ),
    },
    {
      key: "title",
      header: "Name",
      render: (_: any, item: Project) => (
        <div className="d-flex align-items-center">
          <div className="h-30 w-30 d-flex-center b-r-50 overflow-hidden me-2">
            <img src={item.logo} alt={item.title} className="img-fluid" />
          </div>
          <div>
            <h6 className="f-s-15 mb-0">{item.title}</h6>
            <p className="text-secondary f-s-13 mb-0">{item.date}</p>
          </div>
        </div>
      ),
    },
    { key: "creator", header: "Leader", className: "text-dark f-w-500" },
    {
      key: "status",
      header: "Status",
      render: (value: string) => (
        <span
          className={`badge bg-${
            value === "New"
              ? "primary"
              : value === "Inprogress"
                ? "warning"
                : "success"
          }`}
        >
          {value}
        </span>
      ),
    },
    { key: "manager", header: "Client" },
    {
      key: "startDate",
      header: "Start Date",
      className: "text-success f-w-500",
    },
    { key: "endDate", header: "End Date", className: "text-danger f-w-500" },
  ];

  const projectsWithIds = projects.map((p) => ({ ...p, _id: p.id }));

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Advance Table"
          title=" Table "
          path={["Advance Table"]}
          Icon={Table2Columns}
        />
        <Row>
          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>Drag And Drop Table</h5>
              </CardHeader>
              <CardBody className="p-0">
                <div className="table-responsive app-scroll">
                  <Table className="table table-bottom-border advance-drag-drop-table align-middle mb-0">
                    <thead>
                      <tr>
                        <th>
                          <IconArrowsMove
                            size={24}
                            className="text-secondary"
                          />
                        </th>
                        <th>Employee Name</th>
                        <th>Position</th>
                        <th>Status</th>
                        <th>Email</th>
                        <th>id</th>
                        <th>Salary</th>
                        <th>Date</th>
                        <th>Action</th>
                      </tr>
                    </thead>
                    <tbody>
                      {employees.map((employee, index) => (
                        <tr
                          key={index}
                          draggable="true"
                          onDragStart={(e) => handleDragStart(e, index)}
                          onDragOver={handleDragOver}
                          onDrop={(e) => handleDrop(e, index)}
                          onDragEnd={handleDragEnd}
                          style={{
                            cursor: "move",
                            opacity: draggedIndex === index ? 0.5 : 1,
                          }}
                        >
                          <td>
                            <IconArrowsMove
                              size={24}
                              className="text-secondary"
                            />
                          </td>
                          <td>{employee.name}</td>
                          <td className="f-w-600">{employee.position}</td>
                          <td>
                            <span
                              className={`badge text-outline-${
                                employee.status === "High"
                                  ? "success"
                                  : employee.status === "Medium"
                                    ? "warning"
                                    : "danger"
                              }`}
                            >
                              {employee.status}
                            </span>
                          </td>
                          <td>{employee.email}</td>
                          <td className="f-w-500 text-primary">
                            {employee.id}
                          </td>
                          <td className="f-w-500 text-warning">
                            {employee.salary}
                          </td>
                          <td>{employee.date}</td>
                          <td>
                            <button
                              type="button"
                              className="btn btn-danger icon-btn b-r-4"
                            >
                              <IconTrash size={18} />
                            </button>{" "}
                            <button
                              type="button"
                              className="btn btn-success icon-btn b-r-4"
                            >
                              <IconEdit size={18} />
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </Table>
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col xs={12}>
            <CustomDataTable
              title="Projects"
              description=""
              columns={projectColumns}
              data={projectsWithIds}
              rowKey="_id"
              showActions
              onEdit={handleEditClick}
              onDelete={handleDeleteClick}
            />
          </Col>
        </Row>
      </Container>

      {/* === Edit Project Modal === */}
      <Modal isOpen={isModalOpen} toggle={toggleModal} centered>
        <ModalHeader toggle={toggleModal}>Edit Project</ModalHeader>
        <ModalBody>
          <Form>
            <FormGroup>
              <Label for="projectTitle">Title</Label>
              <Input
                id="projectTitle"
                defaultValue={selectedProject?.title || ""}
              />
            </FormGroup>
            <FormGroup>
              <Label for="projectLeader">Leader</Label>
              <Input
                id="projectLeader"
                defaultValue={selectedProject?.creator || ""}
              />
            </FormGroup>
            <FormGroup>
              <Label for="projectStatus">Status</Label>
              <Input
                id="projectStatus"
                defaultValue={selectedProject?.status || ""}
              />
            </FormGroup>
          </Form>
        </ModalBody>
        <ModalFooter>
          <Button color="secondary" onClick={toggleModal}>
            Cancel
          </Button>
          <Button color="primary" onClick={toggleModal}>
            Save Changes
          </Button>
        </ModalFooter>
      </Modal>

      {/* === Delete Confirmation Modal === */}
      <Modal
        isOpen={showDeleteModal}
        toggle={() => setShowDeleteModal(false)}
        centered
      >
        <ModalBody className="text-center">
          <img
            alt="delete"
            className="img-fluid mb-3"
            src="/images/icons/delete-icon.png"
          />
          <h4 className="text-danger fw-bold">Are You Sure?</h4>
          <p className="text-secondary fs-6">
            You won&#39;t be able to revert this!
          </p>
          <div className="mt-3">
            <Button
              color="secondary"
              onClick={() => setShowDeleteModal(false)}
              className="me-2"
            >
              Close
            </Button>
            <Button color="primary" onClick={confirmDelete}>
              Yes, Delete it
            </Button>
          </div>
        </ModalBody>
      </Modal>
    </div>
  );
};

export default AdvanceTablePage;
