import React, { useState } from "react";
import {
  Button,
  Card,
  CardBody,
  Input,
  Label,
  Modal,
  ModalHeader,
  ModalBody,
  ModalFooter,
  Table,
  FormGroup,
  Form,
  Col,
} from "reactstrap";
import { IconSearch, IconEdit, IconTrash } from "@tabler/icons-react";

interface TodoItem {
  id: number;
  task: string;
  priority: "High" | "Medium" | "Low";
  assign: string;
  date: string;
  notes: string;
}

const initialTodos: TodoItem[] = [
  {
    id: 1,
    task: "Design the homepage layout",
    priority: "High",
    assign: "Alex",
    date: "2021-03-19",
    notes: "Revamp homepage design",
  },
  {
    id: 2,
    task: "Set up a meeting with the development team",
    priority: "High",
    assign: "Maria",
    date: "2020-01-19",
    notes: "Gather all invoices",
  },
  {
    id: 3,
    task: "Create marketing strategy for Q2",
    priority: "Medium",
    assign: "John",
    date: "2021-04-10",
    notes: "Focus on social media",
  },
  {
    id: 4,
    task: "Fix bugs reported by QA",
    priority: "High",
    assign: "Nina",
    date: "2021-03-25",
    notes: "Check login and dashboard issues",
  },
  {
    id: 5,
    task: "Update user onboarding flow",
    priority: "Low",
    assign: "Leo",
    date: "2021-03-30",
    notes: "Simplify registration process",
  },
  {
    id: 6,
    task: "Conduct customer satisfaction survey",
    priority: "Medium",
    assign: "Ava",
    date: "2021-04-05",
    notes: "Use Typeform for the survey",
  },
  {
    id: 7,
    task: "Optimize images on product pages",
    priority: "Low",
    assign: "Sophia",
    date: "2021-03-22",
    notes: "Reduce image sizes to under 200KB",
  },
  {
    id: 8,
    task: "Prepare financial report Q1",
    priority: "High",
    assign: "David",
    date: "2021-04-01",
    notes: "Include new revenue streams",
  },
];

const TodoListComponent: React.FC = () => {
  const [todos, setTodos] = useState<TodoItem[]>(initialTodos);
  const [modal, setModal] = useState(false);
  const [formData, setFormData] = useState<Partial<TodoItem>>({});
  const [isEditing, setIsEditing] = useState(false);

  const toggleModal = () => setModal(!modal);

  const handleInputChange = (
    e: React.ChangeEvent<
      HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement
    >
  ) => {
    const { id, value } = e.target;
    setFormData({ ...formData, [id]: value });
  };

  const handleAdd = () => {
    if (formData.task && formData.assign && formData.date) {
      const newTodo: TodoItem = {
        id: Date.now(),
        task: formData.task,
        priority: formData.priority || "Low",
        assign: formData.assign,
        date: formData.date,
        notes: formData.notes || "",
      };
      setTodos([...todos, newTodo]);
      toggleModal();
      setFormData({});
    }
  };

  const handleEdit = () => {
    if (formData.id && formData.task && formData.assign && formData.date) {
      setTodos(
        todos.map((todo) =>
          todo.id === formData.id
            ? ({
                ...todo,
                ...formData,
              } as TodoItem)
            : todo
        )
      );
      toggleModal();
      setFormData({});
      setIsEditing(false);
    }
  };

  const handleDelete = (id: number) => {
    setTodos(todos.filter((todo) => todo.id !== id));
  };

  const openEditModal = (todo: TodoItem) => {
    setFormData(todo);
    setIsEditing(true);
    setModal(true);
  };

  return (
    <Col xl="9">
      <Card className="shadow-sm">
        <CardBody className="p-0">
          <div className="d-flex justify-content-between p-3 border-b">
            <Form className="me-3 app-form app-icon-form search-lg h-100">
              <div className="position-relative h-100">
                <Input
                  type="search"
                  placeholder="Search..."
                  className="form-control search h-100 pe-4"
                />
                <IconSearch
                  className="position-absolute end-0 top-50 translate-middle-y me-2"
                  size={16}
                />
              </div>
            </Form>
            <Button
              color="primary"
              onClick={() => {
                toggleModal();
                setIsEditing(false);
              }}
            >
              Add
            </Button>
          </div>

          <Table hover responsive className="table-bottom-border todo-table">
            <thead>
              <tr>
                <th>Task</th>
                <th>Priority</th>
                <th>Assign</th>
                <th>Date</th>
                <th>Notes</th>
                <th>Edit</th>
                <th>Delete</th>
              </tr>
            </thead>
            <tbody>
              {todos.map((todo) => (
                <tr key={todo.id}>
                  <td className="f-w-600">{todo.task}</td>
                  <td>
                    <span
                      className={`badge bg-${todo.priority === "High" ? "success" : todo.priority === "Medium" ? "warning" : "danger"}`}
                    >
                      {todo.priority}
                    </span>
                  </td>
                  <td className="f-w-500 text-dark">{todo.assign}</td>
                  <td className="text-success f-w-600">{todo.date}</td>
                  <td>{todo.notes}</td>
                  <td>
                    <Button
                      color="success"
                      outline
                      size="sm"
                      className="edit-item-btn icon-btn btn-outline-success"
                      onClick={() => openEditModal(todo)}
                    >
                      <IconEdit size={14} />
                    </Button>
                  </td>
                  <td>
                    <Button
                      color="danger"
                      outline
                      size="sm"
                      className="remove-item-btn icon-btn btn-outline-danger"
                      onClick={() => handleDelete(todo.id)}
                    >
                      <IconTrash size={14} />
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </Table>

          <Modal isOpen={modal} toggle={toggleModal}>
            <ModalHeader toggle={toggleModal}>
              {isEditing ? "Edit Task" : "Add Task"}
            </ModalHeader>
            <ModalBody>
              <Form className="app-form">
                <FormGroup>
                  <Label for="task">Task</Label>
                  <Input
                    id="task"
                    value={formData.task || ""}
                    onChange={handleInputChange}
                    required
                  />
                </FormGroup>
                <FormGroup>
                  <Label for="assign">Assign</Label>
                  <Input
                    id="assign"
                    value={formData.assign || ""}
                    onChange={handleInputChange}
                    required
                  />
                </FormGroup>
                <FormGroup>
                  <Label for="date">Date</Label>
                  <Input
                    id="date"
                    type="date"
                    value={formData.date || ""}
                    onChange={handleInputChange}
                    required
                  />
                </FormGroup>
                <FormGroup>
                  <Label for="notes">Notes</Label>
                  <Input
                    id="notes"
                    type="textarea"
                    value={formData.notes || ""}
                    onChange={handleInputChange}
                  />
                </FormGroup>
                <FormGroup>
                  <Label for="priority">Priority</Label>
                  <Input
                    type="select"
                    id="priority"
                    value={formData.priority || ""}
                    onChange={handleInputChange}
                  >
                    <option value="">Select Priority</option>
                    <option value="High">High</option>
                    <option value="Medium">Medium</option>
                    <option value="Low">Low</option>
                  </Input>
                </FormGroup>
              </Form>
            </ModalBody>
            <ModalFooter>
              <Button color="secondary" onClick={toggleModal}>
                Cancel
              </Button>
              {isEditing ? (
                <Button color="success" onClick={handleEdit}>
                  Update
                </Button>
              ) : (
                <Button color="primary" onClick={handleAdd}>
                  Add
                </Button>
              )}
            </ModalFooter>
          </Modal>
        </CardBody>
      </Card>
    </Col>
  );
};

export default TodoListComponent;
