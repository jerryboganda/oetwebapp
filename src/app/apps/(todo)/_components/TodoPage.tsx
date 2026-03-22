"use client";
import React, { useState } from "react";
import { Container, Form, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  Button,
  Card,
  CardBody,
  Modal,
  ModalHeader,
  ModalBody,
  ModalFooter,
  Input,
  Label,
} from "reactstrap";
import { IconPlus, IconStack2, IconTrash } from "@tabler/icons-react";
import ToDoTable from "@/app/apps/(todo)/_components/ToDoTable";

const defaultTasks = [
  "PolytronX & Dashboard",
  "Project Management",
  "Chat Application",
  "Todo App",
  "React Weather App",
  "Tic-Tac-Toe",
  "Stopwatch",
  "Calculator App",
  "Ecommerce Site",
  "Chat Application",
];
const TodoPage = () => {
  const [modal, setModal] = useState(false);
  const [tasks, setTasks] = useState<string[]>(defaultTasks);
  const [newTask, setNewTask] = useState("");

  const toggleModal = () => setModal(!modal);

  const handleAddTask = () => {
    if (newTask.trim()) {
      setTasks([newTask, ...tasks]);
      setNewTask("");
      toggleModal();
    }
  };

  const handleDeleteTask = (index: number) => {
    setTasks(tasks.filter((_, i) => i !== index));
  };
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Todo"
          title="Apps"
          path={["Todo"]}
          Icon={IconStack2}
        />
        <Row>
          <div className="col-xl-3">
            <Card>
              <CardBody>
                <Button
                  color="primary"
                  size="lg"
                  className="w-100 rounded"
                  onClick={toggleModal}
                >
                  <IconPlus className="me-1" size={18} /> Add Project
                </Button>

                <Modal isOpen={modal} toggle={toggleModal} centered>
                  <ModalHeader toggle={toggleModal}>Create Task</ModalHeader>
                  <ModalBody>
                    <Form className="app-form">
                      <Label for="taskName">Task Name</Label>
                      <Input
                        id="taskName"
                        placeholder="Default input"
                        value={newTask}
                        onChange={(e) => setNewTask(e.target.value)}
                      />
                    </Form>
                  </ModalBody>
                  <ModalFooter>
                    <Button color="primary" onClick={handleAddTask}>
                      Save changes
                    </Button>
                  </ModalFooter>
                </Modal>

                <div className="todo-container mt-4">
                  {tasks.map((task, index) => (
                    <div
                      className="d-flex justify-content-between align-items-center task mb-2"
                      key={index}
                    >
                      <span>{task}</span>
                      <Button
                        color="link"
                        size="sm"
                        className="p-1 border-0"
                        onClick={() => handleDeleteTask(index)}
                      >
                        <IconTrash className="text-danger" size={18} />
                      </Button>
                    </div>
                  ))}
                </div>
              </CardBody>
            </Card>
          </div>
          <ToDoTable />
        </Row>
      </Container>
    </div>
  );
};

export default TodoPage;
