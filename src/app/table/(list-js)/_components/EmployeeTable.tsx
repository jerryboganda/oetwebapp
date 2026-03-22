import React, { useEffect, useRef, useState } from "react";
import {
  Button,
  Input,
  Modal,
  ModalBody,
  ModalFooter,
  ModalHeader,
  Table,
  Badge,
  Form,
  FormGroup,
  Label,
  Col,
  CardHeader,
  Card,
  CardBody,
} from "reactstrap";

interface Employee {
  id: number;
  name: string;
  email: string;
  contact: string;
  date: string;
  status: "ACTIVE" | "BLOCK";
}

const EmployeeTable = () => {
  const [employees, setEmployees] = useState<Employee[]>([
    {
      id: 1,
      name: "Allie Grater",
      email: "graterallie@gmail.com",
      contact: "8054478398",
      date: "2021-03-19",
      status: "BLOCK",
    },
    {
      id: 2,
      name: "Rhoda Report",
      email: "reportrhoda@gmail.com",
      contact: "7765392112",
      date: "2020-01-19",
      status: "ACTIVE",
    },
    {
      id: 3,
      name: "Rose Bush",
      email: "rose@gmail.com",
      contact: "9674903425",
      date: "2020-10-26",
      status: "ACTIVE",
    },
    {
      id: 4,
      name: "Dave Allippa",
      email: "dave@gmail.com",
      contact: "6490537289",
      date: "2020-06-19",
      status: "BLOCK",
    },
  ]);

  const [modal, setModal] = useState(false);
  const [editIndex, setEditIndex] = useState<number | null>(null);
  const [form, setForm] = useState<Employee>({
    id: 0,
    name: "",
    email: "",
    contact: "",
    date: "",
    status: "ACTIVE",
  });

  const listRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (listRef.current) {
      import("list.js").then((List) => {
        new List.default(listRef.current!, {
          valueNames: ["name", "email", "contact", "date", "status"],
        });
      });
    }
  }, [employees]);

  const toggle = () => setModal(!modal);

  const handleChange = (
    e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>
  ) => {
    setForm({ ...form, [e.target.name]: e.target.value });
  };

  const handleSubmit = () => {
    if (editIndex !== null) {
      const updated = [...employees];
      updated[editIndex] = form;
      setEmployees(updated);
    } else {
      setEmployees([...employees, { ...form, id: Date.now() }]);
    }
    toggle();
    setForm({
      id: 0,
      name: "",
      email: "",
      contact: "",
      date: "",
      status: "ACTIVE",
    });
    setEditIndex(null);
  };

  const handleEdit = (index: number) => {
    setForm(employees[index] as Employee);
    setEditIndex(index);
    toggle();
  };

  const handleDelete = (index: number) => {
    const updated = employees.filter((_, i) => i !== index);
    setEmployees(updated);
  };

  return (
    <>
      <Col lg={8}>
        <Card>
          <CardHeader>
            <h5>Add, Edit & Remove table</h5>
          </CardHeader>
          <CardBody className="p-0">
            <div id="employee-list" ref={listRef}>
              <div className="list-table-header d-flex justify-content-sm-between">
                <div className="flex-grow-1 w-100">
                  <Button
                    color="primary"
                    onClick={() => toggle()}
                    className="mb-3 rounded-pill px-4"
                  >
                    Add
                  </Button>
                </div>
                <Input className="search mb-3" placeholder="Search..." />
              </div>

              <Table className="table table-bottom-border  list-table-data align-middle mb-0">
                <thead>
                  <tr>
                    <th></th>
                    <th>Employee</th>
                    <th>Email</th>
                    <th>Contact</th>
                    <th>Joining Date</th>
                    <th>Status</th>
                    <th>Edit</th>
                    <th>Delete</th>
                  </tr>
                </thead>
                <tbody className="list">
                  {employees.length === 0 ? (
                    <tr>
                      <td colSpan={8} className="text-center py-4 text-muted">
                        No employees found
                      </td>
                    </tr>
                  ) : (
                    employees.map((emp, i) => (
                      <tr key={emp.id}>
                        <td>
                          <Input type="checkbox" />
                        </td>
                        <td className="name">{emp.name}</td>
                        <td className="email">{emp.email}</td>
                        <td className="contact">{emp.contact}</td>
                        <td className="date">{emp.date}</td>
                        <td className="status">
                          <Badge
                            color={
                              emp.status === "ACTIVE"
                                ? "light-success"
                                : "light-danger"
                            }
                          >
                            {emp.status}
                          </Badge>
                        </td>
                        <td>
                          <Button
                            color="success"
                            onClick={() => handleEdit(i)}
                            className="rounded-pill px-3"
                          >
                            Edit
                          </Button>
                        </td>
                        <td>
                          <Button
                            color="danger"
                            onClick={() => handleDelete(i)}
                            className="rounded-pill px-3"
                          >
                            Remove
                          </Button>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </Table>

              <div className="list-pagination">
                <ul className="pagination"></ul>
              </div>
            </div>
          </CardBody>
        </Card>
      </Col>

      <Modal isOpen={modal} toggle={toggle} centered>
        <ModalHeader toggle={toggle}>
          {editIndex !== null ? "Edit Employee" : "Add Employee"}
        </ModalHeader>
        <ModalBody>
          <Form className="app-form">
            <FormGroup>
              <Label>Name</Label>
              <Input name="name" value={form.name} onChange={handleChange} />
            </FormGroup>
            <FormGroup>
              <Label>Email</Label>
              <Input name="email" value={form.email} onChange={handleChange} />
            </FormGroup>
            <FormGroup>
              <Label>Contact</Label>
              <Input
                name="contact"
                value={form.contact}
                onChange={handleChange}
              />
            </FormGroup>
            <FormGroup>
              <Label>Joining Date</Label>
              <Input
                type="date"
                name="date"
                value={form.date}
                onChange={handleChange}
              />
            </FormGroup>
            <FormGroup>
              <Label>Status</Label>
              <Input
                type="select"
                name="status"
                value={form.status}
                onChange={handleChange}
              >
                <option value="ACTIVE">ACTIVE</option>
                <option value="BLOCK">BLOCK</option>
              </Input>
            </FormGroup>
          </Form>
        </ModalBody>
        <ModalFooter>
          <Button color="primary" onClick={handleSubmit}>
            {editIndex !== null ? "Update" : "Add"}
          </Button>{" "}
          <Button color="secondary" onClick={toggle}>
            Cancel
          </Button>
        </ModalFooter>
      </Modal>
    </>
  );
};

export default EmployeeTable;
